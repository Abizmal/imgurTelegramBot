﻿using System.Data.Entity;

namespace ImgurTelegramBot.Webhooks.Models
{
    public class ImgurDbContext: DbContext
    {
        public ImgurDbContext(): base("name=ImgurDbConnectionString") { }

        public DbSet<User> Users { get; set; }
        public DbSet<ImgurSetting> ImgurSettings { get; set; }
    }
}