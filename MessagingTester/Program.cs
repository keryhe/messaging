using Keryhe.Messaging.Polling;
using Keryhe.Messaging.Polling.Delay;

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

                    services.Configure<FileSystemPollerOptions>(hostContext.Configuration.GetSection("FileSystemPollerOptions"));
                    services.Configure<ConstantOptions>(hostContext.Configuration.GetSection("ConstantOptions"));

                    services.AddTransient<IDelay, ConstantDelay>();
                    services.AddTransient<IPoller<List<string>>, FileSystemPoller>();

                    services.AddHostedService<Worker>();
                })
                .Build();

            host.Run();
        }
    }


   
}