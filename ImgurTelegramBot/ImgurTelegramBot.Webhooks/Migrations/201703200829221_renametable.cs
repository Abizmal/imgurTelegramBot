namespace ImgurTelegramBot.Webhooks.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class renametable : DbMigration
    {
        public override void Up()
        {
            RenameTable(name: "dbo.Settings", newName: "ImgurSettings");
        }
        
        public override void Down()
        {
            RenameTable(name: "dbo.ImgurSettings", newName: "Settings");
        }
    }
}
