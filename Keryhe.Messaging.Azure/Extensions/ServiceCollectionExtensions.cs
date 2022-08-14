using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Keryhe.Messaging.Azure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddServiceBusListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddTransient<IMessageListener<T>, ServiceBusListener<T>>();
            services.Configure<ServiceBusListenerOptions>(config);

            return services;
        }

        public static IServiceCollection AddServiceBusPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddTransient<IMessagePublisher<T>, ServiceBusPublisher<T>>();
            services.Configure<ServiceBusPublisherOptions>(config);

            return services;
        }
    }
}
