using System.Data.Entity;

namespace ImgurTelegramBot.Models
{
    public class ImgurDbContext: DbContext
    {
        public ImgurDbContext(): base("name=ImgurDbConnectionString") { }

        public DbSet<User> Users { get; set; }
        public DbSet<Setting> Settings { get; set; }
    }
}