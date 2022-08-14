using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Keryhe.Messaging.AWS.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSQSListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddTransient<IMessageListener<T>, SQSListener<T>>();
            services.Configure<SQSListenerOptions>(config);

            return services;
        }

        public static IServiceCollection AddServiceBusPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddTransient<IMessagePublisher<T>, SQSPublisher<T>>();
            services.Configure<SQSPublisherOptions>(config);

            return services;
        }
    }
}
