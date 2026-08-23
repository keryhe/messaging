using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Keryhe.Messaging.Azure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddServiceBusListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton<IMessageListener<T>>(sp => new ServiceBusListener<T>(
                sp.GetRequiredService<IOptionsMonitor<ServiceBusListenerOptions>>(),
                sp.GetRequiredService<ILogger<ServiceBusListener<T>>>()));
            services.Configure<ServiceBusListenerOptions>(config);

            return services;
        }

        public static IServiceCollection AddServiceBusPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton<IMessagePublisher<T>>(sp => new ServiceBusPublisher<T>(
                sp.GetRequiredService<IOptionsMonitor<ServiceBusPublisherOptions>>(),
                sp.GetRequiredService<ILogger<ServiceBusPublisher<T>>>()));
            services.Configure<ServiceBusPublisherOptions>(config);

            return services;
        }
    }
}
