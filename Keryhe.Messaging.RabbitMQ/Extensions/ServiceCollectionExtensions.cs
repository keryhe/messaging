using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Keryhe.Messaging.RabbitMQ.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddRabbitMQListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddTransient<IMessageListener<T>, RabbitMQListener<T>>();
            services.Configure<RabbitMQOptions>(config);

            return services;
        }

        public static IServiceCollection AddRabbitMQPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddTransient<IMessagePublisher<T>, RabbitMQPublisher<T>>();
            services.Configure<RabbitMQOptions>(config);

            return services;
        }
    }
}
