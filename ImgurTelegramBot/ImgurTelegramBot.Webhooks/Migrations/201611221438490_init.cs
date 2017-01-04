using System.Data.Entity.Migrations;

namespace ImgurTelegramBot.Webhooks.Migrations
{

    public partial class init : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Settings",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        Offset = c.Int(nullable: false),
                        MaximumFileSize = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "dbo.Users",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        Username = c.String(),
                        ChatId = c.Long(nullable: false),
                        Created = c.DateTime(nullable: false),
                        MessagesCount = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.Users");
            DropTable("dbo.Settings");
        }
    }
}
