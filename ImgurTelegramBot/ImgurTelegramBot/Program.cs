using Quartz;
using Quartz.Impl;

namespace ImgurTelegramBot
{
    class Program
    {
        static void Main(string[] args)
        {
            TelegramJob.ScheduleJob();
        }
    }

}
