using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Keryhe.Messaging.RabbitMQ.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddRabbitMQListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddOptions();
            services.AddSingleton<IMessageListener<T>>(sp => new RabbitMQListener<T>(
                sp.GetRequiredService<IOptionsMonitor<RabbitMQListenerOptions>>(),
                sp.GetRequiredService<ILogger<RabbitMQListener<T>>>()));
            services.Configure<RabbitMQListenerOptions>(config);

            return services;
        }

        public static IServiceCollection AddRabbitMQPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddOptions();
            services.AddSingleton<IMessagePublisher<T>>(sp => new RabbitMQPublisher<T>(
                sp.GetRequiredService<IOptionsMonitor<RabbitMQPublisherOptions>>(),
                sp.GetRequiredService<ILogger<RabbitMQPublisher<T>>>()));
            services.Configure<RabbitMQPublisherOptions>(config);

            return services;
        }

        public static IServiceCollection AddRabbitMQListener<T>(this IServiceCollection services)
        {
            services.AddOptions();
            services.AddSingleton<IMessageListener<T>>(sp => new RabbitMQListener<T>(
                sp.GetRequiredService<IOptionsMonitor<RabbitMQListenerOptions>>(),
                sp.GetRequiredService<ILogger<RabbitMQListener<T>>>()));

            return services;
        }

        public static IServiceCollection AddRabbitMQPublisher<T>(this IServiceCollection services)
        {
            services.AddOptions();
            services.AddSingleton<IMessagePublisher<T>>(sp => new RabbitMQPublisher<T>(
                sp.GetRequiredService<IOptionsMonitor<RabbitMQPublisherOptions>>(),
                sp.GetRequiredService<ILogger<RabbitMQPublisher<T>>>()));

            return services;
        }
    }
}
