using Keryhe.Messaging.RabbitMQ.Factory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Keryhe.Messaging.RabbitMQ.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddRabbitMQListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.TryAddSingleton<IRabbitMQConnection, RabbitMQConnection>();
            services.AddTransient<IMessageListener<T>, RabbitMQListener<T>>();
            services.Configure<RabbitMQListenerOptions>(config.GetSection("Keryhe.Messaging.RabbitMQ.RabbitMQFactoryOptions"));
            services.Configure<RabbitMQFactoryOptions>(config.GetSection("Keryhe.Messaging.RabbitMQ.RabbitMQListenerOptions"));

            return services;
        }

        public static IServiceCollection AddRabbitMQPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.TryAddSingleton<IRabbitMQConnection, RabbitMQConnection>();
            services.AddTransient<IMessagePublisher<T>, RabbitMQPublisher<T>>();
            services.Configure<RabbitMQListenerOptions>(config.GetSection("Keryhe.Messaging.RabbitMQ.RabbitMQFactoryOptions"));
            services.Configure<RabbitMQFactoryOptions>(config.GetSection("Keryhe.Messaging.RabbitMQ.RabbitMQPublisherOptions"));

            return services;
        }
    }
}
