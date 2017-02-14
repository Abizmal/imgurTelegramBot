using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
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
        private const string GfyCatBaseUrl = "https://api.gfycat.com/v1/gfycats/";

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
                var data = update.CallbackQuery.Data.Split(' ');

                switch(data[0])
                {
                    case "imgur":
                        if (!IsLimitRemains(out reset, out statusCode))
                        {
                            ImgurLimitProblems(update, statusCode, reset);
                            return;
                        }

                        if (DeleteImage(data[1]))
                        {
                            _bot.AnswerCallbackQueryAsync(update.CallbackQuery.Id, "Deleted");
                            var result = _bot.EditMessageReplyMarkupAsync(update.CallbackQuery.Message.Chat.Id, update.CallbackQuery.Message.MessageId).Result;
                        }
                        break;
                    case "gfycat":
//                        if (DeleteVideo(data[1]))
//                        {
//                            _bot.AnswerCallbackQueryAsync(update.CallbackQuery.Id, "Deleted");
//                            var result = _bot.EditMessageReplyMarkupAsync(update.CallbackQuery.Message.Chat.Id, update.CallbackQuery.Message.MessageId).Result;
//                        }
                        break;
                }
            }
            else
            {
                if(SetStats(update) && !string.IsNullOrEmpty(update.Message.Text) && update.Message.Text.Equals("/start"))
                {
                    _bot.SendTextMessageAsync(update.Message.Chat.Id, "Welcome! Just forward me a message with an image or upload one");
                }
                var fileId = GetFileId(update);
                var currentType = update.Message.Document?.MimeType?.Split('/')[0];

                if (fileId != null)
                {
                    var media = _bot.GetFileAsync(fileId).Result;
                    var title = update.Message.Caption;
                    using(media.FileStream)
                    {
                        var bytes = ReadFully(media.FileStream);
                        switch(currentType)
                        {
                            case "video":
                                _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Please, wait, it may take few seconds");
                                var result = UploadVideo(bytes);

                                if (result != null)
                                {
//                                    var button = new InlineKeyboardButton("Delete", "gfycat " + status.gfyId);
//                                    var markup = new InlineKeyboardMarkup();
//                                    markup.InlineKeyboard = new[] { new[] { button } };
//
                                    _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Here is direct link for your gif:{result.gifUrl} \n And here is webm version: {result.webmUrl}", disableWebPagePreview: true /*, replyMarkup: markup*/);
                                    return;
                                }
                                break;
                            case "image":
                                if (!IsLimitRemains(out reset, out statusCode))
                                {
                                    ImgurLimitProblems(update, statusCode, reset);
                                    return;
                                }

                                _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Please, wait, it may take few seconds");
                                var uploadResult = UploadPhoto(bytes, title);
                                if (!string.IsNullOrEmpty(uploadResult?.Link))
                                {
                                    var button = new InlineKeyboardButton("Delete", "imgur " + uploadResult.DeleteHash);
                                    var markup = new InlineKeyboardMarkup();
                                    markup.InlineKeyboard = new[] { new[] { button } };

                                    _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Here is direct link for your image: {uploadResult.Link}", disableWebPagePreview: true, replyMarkup: markup);
                                    return;
                                }
                                break;
                        }
                    }
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

                _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Something went wrong. Please try again. Also remember that your image should be less then 10 MB and be, actually, image or gif.");
            }
        }

        private void ImgurLimitProblems(Update update, HttpStatusCode statusCode, DateTime? reset)
        {
            if(statusCode == HttpStatusCode.ServiceUnavailable)
            {
                Trace.TraceError("statusCode: " + statusCode);
                _bot.SendTextMessageAsync(update.Message.Chat.Id, "Sorry, Imgur is temporarily over capacity. Please try again later.");
                return;
            }

            if(statusCode == HttpStatusCode.OK)
            {
                Trace.TraceError("Reset: " + reset);
                _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Sorry, I've faced an Imgur Rate limit and have to wait {reset - DateTime.UtcNow}");
                return;
            }

            Trace.TraceError("statusCode: " + statusCode);
            _bot.SendTextMessageAsync(update.Message.Chat.Id, "Sorry, Imgur is facing problems. Try again later");
        }


        private GfyItem UploadVideo(byte[] bytes)
        {
            var token = GetGfyCatToken();

            const string uploadUrl = "https://filedrop.gfycat.com";
            const string statusUrl = GfyCatBaseUrl + "fetch/status/";

            using(var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", token.access_token);
                client.DefaultRequestHeaders.Add("ContentType", "application/json");
                var key = client.PostAsync(GfyCatBaseUrl, null).Result;
                var gfyCatKey = JsonConvert.DeserializeObject<GfyCatKey>(key.Content.ReadAsStringAsync().Result);

                using(var content = new MultipartFormDataContent())
                {
                    client.DefaultRequestHeaders.Clear();
                    content.Add(new StringContent(gfyCatKey.gfyname), "key");
                    content.Add(new ByteArrayContent(bytes), "file", gfyCatKey.gfyname);

                    using(var message = client.PostAsync(uploadUrl, content).Result)
                    {
                        if(message.IsSuccessStatusCode)
                        {
                            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token.access_token);

                            var counter = 0;
                            while (true)
                            {
                                if(counter++ == 100)
                                {
                                    return null;
                                }
                                var status = client.GetStringAsync(statusUrl + gfyCatKey.gfyname).Result;
                                var gfyCatStatus = JsonConvert.DeserializeObject<GfyCatStatus>(status);
                                if (!gfyCatStatus.task.ToLower().Equals("complete"))
                                {
                                    Thread.Sleep(100);
                                    continue;
                                }

                                var get = client.GetStringAsync(GfyCatBaseUrl + gfyCatStatus.gfyname.ToLower()).Result;
                                var result = JsonConvert.DeserializeObject<GfyGetResult>(get);

                                return result.gfyItem;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private GfyCatToken GetGfyCatToken()
        {
            const string url = "https://api.gfycat.com/v1/oauth/token";
            var json = $"{{'client_id':'{ConfigurationManager.AppSettings["GfycatClientId"]}', 'client_secret': '{ConfigurationManager.AppSettings["GfycatClientSecret"]}', 'grant_type': 'client_credentials'}}";
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                var response = client.UploadString(url, "POST", json);
                var token = JsonConvert.DeserializeObject<GfyCatToken>(response);
                return token;
            }
        }

        private bool DeleteVideo(string data)
        {
            var token = GetGfyCatToken();

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", token.access_token);
                client.DefaultRequestHeaders.Add("ContentType", "application/json");
                var result = client.DeleteAsync(GfyCatBaseUrl + data).Result;
                return result.IsSuccessStatusCode;
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

            if (update.Message.Document != null && (update.Message.Document.MimeType.StartsWith("image") || update.Message.Document.MimeType.StartsWith("video")))
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