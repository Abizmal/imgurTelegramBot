using System;

namespace ImgurTelegramBot.Webhooks.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public long ChatId { get; set; }
        public DateTime Created { get; set; }
        public int MessagesCount { get; set; }
    }
}