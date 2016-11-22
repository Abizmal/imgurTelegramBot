using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Imgur.API.Authentication.Impl;
using Imgur.API.Endpoints.Impl;
using Imgur.API.Models;

using ImgurTelegramBot.Models;

using Quartz;

using Telegram.Bot;
using Telegram.Bot.Types;

using File = System.IO.File;

namespace ImgurTelegramBot
{
    public class TelegramJob: IJob
    {
        private readonly TelegramBotClient _bot = new TelegramBotClient(ConfigurationManager.AppSettings["Token"]);
        private int _maximumFileSize;

        public void Execute(IJobExecutionContext context)
        {
            using(var db = new ImgurDbContext())
            {
                try
                {
                    var setting = db.Settings.FirstOrDefault();
                    var offset = setting.Offset;
                    _maximumFileSize = setting.MaximumFileSize;

                    var updates = _bot.GetUpdatesAsync(offset).Result;
                    foreach(var update in updates)
                    {
                        ProcessUpdate(update);
                        db.Settings.First().Offset = update.Id + 1;
                        db.SaveChanges();
                    }
                }
                catch(Exception e)
                {
                    Trace.TraceError(e.Message);
                }
            }
        }

        private void ProcessUpdate(Update update)
        {
            SetStats(update);
            var fileId = GetFileId(update);
            if(fileId != null)
            {
                var photo = _bot.GetFileAsync(fileId).Result;
                var title = update.Message.Caption;
                using(photo.FileStream)
                {
                    var uploadResult = UploadPhoto(photo.FileStream, title);
                    if(!string.IsNullOrEmpty(uploadResult?.Link))
                    {
                        _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Here is direct link for you image: {uploadResult.Link}", disableWebPagePreview: true);
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

            if (update.Message.Text.Equals("/stat") && update.Message.Chat.Username.Equals("Immelstorn"))
            {
                using(var db = new ImgurDbContext())
                {
                    var users = db.Users.Count();
                    var last = db.Users.OrderByDescending(u => u.Created).First();
                    _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Users count: {users}, last registred: {last.Username}");
                }
            }

            _bot.SendTextMessageAsync(update.Message.Chat.Id, "Something went wrong. Your image should be less then 10 MB and be, actually, image.");
        }

        private static void SetStats(Update update)
        {
            using(var db = new ImgurDbContext())
            {
                var existingUser = db.Users.FirstOrDefault(u => u.Username.Equals(update.Message.Chat.Username));
                if(existingUser == null)
                {
                    db.Users.Add(new Models.User {
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
        }

        private string GetFileId(Update update)
        {
            if(update.Message.Photo != null && update.Message.Photo.Length > 0)
            {
                var correctSizes = update.Message.Photo.Where(p => p.FileSize <= _maximumFileSize).ToList();
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

        private static IImage UploadPhoto(Stream stream, string title)
        {
            var imgur = new ImgurClient(ConfigurationManager.AppSettings["ImgurClientId"], ConfigurationManager.AppSettings["ImgurClientSecret"]);
            var endpoint = new ImageEndpoint(imgur);
            var result = endpoint.UploadImageStreamAsync(stream, title: title).Result;
            return result;
        }
    }
}