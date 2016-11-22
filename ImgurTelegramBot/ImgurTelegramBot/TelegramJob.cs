using System.Configuration;
using System.IO;
using System.Linq;

using Imgur.API.Authentication.Impl;
using Imgur.API.Endpoints.Impl;
using Imgur.API.Models;

using Quartz;

using Telegram.Bot;
using Telegram.Bot.Types;

using File = System.IO.File;

namespace ImgurTelegramBot
{
    public class TelegramJob: IJob
    {
        private readonly TelegramBotClient _bot = new TelegramBotClient(ConfigurationManager.AppSettings["Token"]);
        private const string OffsetFileName = "offset.txt";
        private const int MaximumFileSize = 10485760; //10MB

        public void Execute(IJobExecutionContext context)
        {
            var updates = _bot.GetUpdatesAsync(GetOffset()).Result;
            foreach(var update in updates)
            {
                ProcessUpdate(update);
                SetOffset(update.Id + 1);
            }
        }

        private void ProcessUpdate(Update update)
        {
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
            var currentType = update.Message.Document.MimeType?.Split('/')[0];
            if(currentType != null && !currentType.Equals("image"))
            {
                _bot.SendTextMessageAsync(update.Message.Chat.Id, $"Something went wrong. Your image should be less then 10 MB and be, actually, image, not {update.Message.Document.MimeType}.");
                return;
            }
            _bot.SendTextMessageAsync(update.Message.Chat.Id, "Something went wrong. Your image should be less then 10 MB and be, actually, image.");
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

        private static IImage UploadPhoto(Stream stream, string title)
        {
            var imgur = new ImgurClient(ConfigurationManager.AppSettings["ImgurClientId"], ConfigurationManager.AppSettings["ImgurClientSecret"]);
            var endpoint = new ImageEndpoint(imgur);
            var result = endpoint.UploadImageStreamAsync(stream, title: title).Result;
            return result;
        }

        private static int GetOffset()
        {
            var result = -1;
            if(File.Exists(OffsetFileName))
            {
                var content = File.ReadAllText(OffsetFileName);
                int.TryParse(content, out result);
            }
            return result;
        }
        private static void SetOffset(int offset)
        {
            File.WriteAllText(OffsetFileName, offset.ToString());
        }
    }
}