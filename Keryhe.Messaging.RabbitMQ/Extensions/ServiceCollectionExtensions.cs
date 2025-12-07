using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Keryhe.Messaging.RabbitMQ.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddRabbitMQListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddOptions();
            services.AddSingleton<IMessageListener<T>, RabbitMQListener<T>>();
            services.Configure<RabbitMQListenerOptions>(config);

            return services;
        }

        public static IServiceCollection AddRabbitMQPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddOptions();
            services.AddSingleton<IMessagePublisher<T>, RabbitMQPublisher<T>>();
            services.Configure<RabbitMQPublisherOptions>(config);

            return services;
        }

        public static IServiceCollection AddRabbitMQListener<T>(this IServiceCollection services)
        {
            services.AddSingleton<IMessageListener<T>, RabbitMQListener<T>>();

            return services;
        }

        public static IServiceCollection AddRabbitMQPublisher<T>(this IServiceCollection services)
        {
            services.AddSingleton<IMessagePublisher<T>, RabbitMQPublisher<T>>();

            return services;
        }
    }
}
