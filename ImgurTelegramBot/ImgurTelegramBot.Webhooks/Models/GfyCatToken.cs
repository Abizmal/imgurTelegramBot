namespace ImgurTelegramBot.Webhooks.Models
{
    public class GfyCatToken
    {
        public string token_type { get; set; }
        public string scope { get; set; }
        public int expires_in { get; set; }
        public string access_token { get; set; }
    }
}