using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Keryhe.Messaging.AWS.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSQSListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton<IMessageListener<T>>(sp => new SQSListener<T>(
                sp.GetRequiredService<IOptionsMonitor<SQSListenerOptions>>(),
                sp.GetRequiredService<ILogger<SQSListener<T>>>()));
            services.Configure<SQSListenerOptions>(config);

            return services;
        }

        public static IServiceCollection AddSQSPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton<IMessagePublisher<T>>(sp => new SQSPublisher<T>(
                sp.GetRequiredService<IOptionsMonitor<SQSPublisherOptions>>(),
                sp.GetRequiredService<ILogger<SQSPublisher<T>>>()));
            services.Configure<SQSPublisherOptions>(config);

            return services;
        }
    }
}
