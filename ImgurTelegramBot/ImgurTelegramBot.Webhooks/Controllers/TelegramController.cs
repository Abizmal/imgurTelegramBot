using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Http;

using Imgur.API.Authentication.Impl;
using Imgur.API.Endpoints.Impl;
using Imgur.API.Models;

using ImgurTelegramBot.Webhooks.Models;

using Newtonsoft.Json;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

using User = ImgurTelegramBot.Webhooks.Models.User;

namespace ImgurTelegramBot.Webhooks.Controllers
{
    public class TelegramController:ApiController
    {
        private readonly TelegramBotClient _bot = new TelegramBotClient(ConfigurationManager.AppSettings["Token"]);
        private const int MaximumFileSize = 10485760;

        public void Post([FromBody]Update update)
        {
            try
            {
                if (update.CallbackQuery != null || update.Message != null)
                {
                    ProcessUpdate(update);
                }
            }
            catch (AggregateException a)
            {
                foreach (var exception in a.InnerExceptions)
                {
                    Trace.TraceError("================================================================");
                    Trace.TraceError(exception.Message);
                    Trace.TraceError(exception.StackTrace);
                }
            }
            catch (Exception e)
            {
                Trace.TraceError("================================================================");
                Trace.TraceError(e.Message);
                Trace.TraceError(e.StackTrace);
            }
        }

        private void ProcessUpdate(Update update)
        {
            DateTime? reset;
            HttpStatusCode statusCode;

            if (update.Type == UpdateType.CallbackQueryUpdate)
            {
                if (!IsLimitRemains(out reset, out statusCode))
                {
                    if(statusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        Trace.TraceError("statusCode: " + statusCode);
                        _bot.SendTextMessageAsync(update.Message.Chat.Id, "Sorry, Imgur is temporarily over capacity. Please try again later.");
                        return;
                    }

                    if (statusCode == HttpStatusCode.OK)
                    {
                        Trace.TraceError("Reset: " + reset);
                        _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Sorry, I've faced an Imgur Rate limit and have to wait {reset - DateTime.UtcNow}");
                        return;
                    }

                    Trace.TraceError("statusCode: " + statusCode);
                    _bot.SendTextMessageAsync(update.Message.Chat.Id, "Sorry, Imgur is facing problems. Try again later");
                    return;
                }

                if (DeleteImage(update.CallbackQuery.Data))
                {

                    _bot.AnswerCallbackQueryAsync(update.CallbackQuery.Id, "Deleted");
                    var result = _bot.EditMessageReplyMarkupAsync(update.CallbackQuery.Message.Chat.Id, update.CallbackQuery.Message.MessageId).Result;
                    return;
                }
            }
            else
            {
                if(SetStats(update) && !string.IsNullOrEmpty(update.Message.Text) && update.Message.Text.Equals("/start"))
                {
                    _bot.SendTextMessageAsync(update.Message.Chat.Id, "Welcome! Just forward me a message with an image");
                }
                var fileId = GetFileId(update);
                if(fileId != null)
                {
                    var photo = _bot.GetFileAsync(fileId).Result;
                    var title = update.Message.Caption;
                    using(photo.FileStream)
                    {
                        var bytes = ReadFully(photo.FileStream);
                        if(!IsLimitRemains(out reset, out statusCode))
                        {
                            if (statusCode == HttpStatusCode.ServiceUnavailable)
                            {
                                Trace.TraceError("statusCode: " + statusCode);
                                _bot.SendTextMessageAsync(update.Message.Chat.Id, "Sorry, Imgur is temporarily over capacity. Please try again later.");
                                return;
                            }

                            if (statusCode == HttpStatusCode.OK)
                            {
                                Trace.TraceError("Reset: " + reset);
                                _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Sorry, I've faced an Imgur Rate limit and have to wait {reset - DateTime.UtcNow}");
                                return;
                            }

                            Trace.TraceError("statusCode: " + statusCode);
                            _bot.SendTextMessageAsync(update.Message.Chat.Id, "Sorry, Imgur is facing problems. Try again later");
                            return;
                        }

                        var uploadResult = UploadPhoto(bytes, title);
                        if (!string.IsNullOrEmpty(uploadResult?.Link))
                        {
                            var button = new InlineKeyboardButton("Delete", uploadResult.DeleteHash);
                            var markup = new InlineKeyboardMarkup();
                            markup.InlineKeyboard = new[] { new[] { button } };

                            _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Here is direct link for your image: {uploadResult.Link}", disableWebPagePreview: true, replyMarkup: markup);
                            return;
                        }
                    }
                }
                var currentType = update.Message.Document?.MimeType?.Split('/')[0];
                if(currentType != null && !currentType.Equals("image"))
                {
                    _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Something went wrong. Your image should be less then 10 MB and be, actually, image, not {update.Message.Document.MimeType}.");
                    return;
                }

                if(!string.IsNullOrEmpty(update.Message.Text) && update.Message.Text.Equals("/stat") && update.Message.Chat.Username.Equals("Immelstorn"))
                {
                    using(var db = new ImgurDbContext())
                    {
                        var users = db.Users.Count();
                        var last = db.Users.OrderByDescending(u => u.Created).First();
                        _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Users count: {users}, last registred: {last.Username}");
                    }
                    return;
                }

                _bot.SendTextMessageAsync(update.Message.Chat.Id, "Something went wrong. Your image should be less then 10 MB and be, actually, image.");
            }
        }

        private static byte[] ReadFully(Stream input)
        {
            using (var ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static bool SetStats(Update update)
        {
            var newUser = false;
            using(var db = new ImgurDbContext())
            {
                var existingUser = db.Users.FirstOrDefault(s => s.ChatId == update.Message.Chat.Id)
                        ?? (update.Message.From.Username != null
                                ? db.Users.FirstOrDefault(s => s.Username == update.Message.From.Username)
                                : null);

                if(existingUser == null)
                {
                    newUser = true;
                    db.Users.Add(new User {
                                     ChatId = update.Message.Chat.Id,
                                     Created = DateTime.Now,
                                     MessagesCount = 1,
                                     Username = update.Message.Chat.Username
                                 });
                }
                else
                {
                    existingUser.MessagesCount++;
                }
                db.SaveChanges();
            }
            return newUser;
        }

        private string GetFileId(Update update)
        {
            if(update.Message.Photo != null && update.Message.Photo.Length > 0)
            {
                var correctSizes = update.Message.Photo.Where(p => p.FileSize <= MaximumFileSize).ToList();
                if(correctSizes.Count == 0)
                {
                    _bot.SendTextMessageAsync(update.Message.Chat.Id, "Image size should be less them 10Mb");
                    return null;
                }
                return correctSizes.First(ph => ph.FileSize == update.Message.Photo.Max(p => p.FileSize)).FileId;
            }

            if (update.Message.Document != null && (update.Message.Document.MimeType.StartsWith("image")))
            {
                return update.Message.Document.FileId;
            }
            return null;
        }

        private static bool IsLimitRemains(out DateTime? reset, out HttpStatusCode statusCode)
        {
            Trace.TraceError("Getting limit");
            reset = null;
            statusCode = HttpStatusCode.OK;
            var imgur = new ImgurClient(ConfigurationManager.AppSettings["ImgurClientId"], ConfigurationManager.AppSettings["ImgurClientSecret"]);
            var endpoint = new ImageEndpoint(imgur);
            var get = endpoint.HttpClient.GetAsync("https://api.imgur.com/3/credits").Result;
            if (get.StatusCode != HttpStatusCode.OK)
            {
                statusCode = get.StatusCode;
                return false;
            }
            var content = get.Content.ReadAsStringAsync().Result;
            var credit = JsonConvert.DeserializeObject<ImgurCredit>(content);
            if(credit?.data != null)
            {
                Trace.TraceError(content);
                reset = UnixTimeStampToDateTime(credit.data.UserReset);
                Trace.TraceError("reset: " + reset);

                if (credit.data.UserRemaining > 0)
                {
                    return true;
                }
            }
            Trace.TraceError("Credit data is null!");
            return false;
        }

        private static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            var dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        private static IImage UploadPhoto(byte[] stream, string title)
        {
            var imgur = new ImgurClient(ConfigurationManager.AppSettings["ImgurClientId"], ConfigurationManager.AppSettings["ImgurClientSecret"]);
            var endpoint = new ImageEndpoint(imgur);
            var result = endpoint.UploadImageBinaryAsync(stream, title: title).Result;
            return result;
        }

        private static bool DeleteImage(string data)
        {
            var imgur = new ImgurClient(ConfigurationManager.AppSettings["ImgurClientId"], ConfigurationManager.AppSettings["ImgurClientSecret"]);
            var endpoint = new ImageEndpoint(imgur);
            var result = endpoint.DeleteImageAsync(data).Result;
            return result;
        }
    }
}