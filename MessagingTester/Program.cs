using Keryhe.Messaging;
using Microsoft.Extensions.Options;

namespace MessagingTester
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddOptions();
                    
                    services.AddHostedService<Worker>();
                })
                .Build();

            host.Run();
        }
    }


   
}