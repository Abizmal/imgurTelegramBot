using System.Data.Entity.Migrations;
using System.Linq;

using ImgurTelegramBot.Webhooks.Models;

namespace ImgurTelegramBot.Webhooks.Migrations
{

    internal sealed class Configuration : DbMigrationsConfiguration<ImgurDbContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
        }

        protected override void Seed(ImgurDbContext context)
        {
            if(!context.ImgurSettings.Any())
            {
                context.ImgurSettings.Add(new ImgurSetting {
                                         Offset = 169828489,
                                         MaximumFileSize = 10485760
                                     });
                context.SaveChanges();
            }
            //  This method will be called after migrating to the latest version.

            //  You can use the DbSet<T>.AddOrUpdate() helper extension method 
            //  to avoid creating duplicate seed data. E.g.
            //
            //    context.People.AddOrUpdate(
            //      p => p.FullName,
            //      new Person { FullName = "Andrew Peters" },
            //      new Person { FullName = "Brice Lambson" },
            //      new Person { FullName = "Rowan Miller" }
            //    );
            //
        }
    }
}
