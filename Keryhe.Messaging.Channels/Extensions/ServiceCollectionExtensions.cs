using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Keryhe.Messaging.Channels.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddChannelPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.TryAddSingleton<IChannelRegistry<T>, ChannelRegistry<T>>();
            services.AddTransient<IMessagePublisher<T>, ChannelPublisher<T>>();
            services.Configure<ChannelPublisherOptions>(config);

            return services;
        }

        public static IServiceCollection AddChannelListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.TryAddSingleton<IChannelRegistry<T>, ChannelRegistry<T>>();
            services.AddTransient<IMessageListener<T>, ChannelListener<T>>();
            services.Configure<ChannelListenerOptions>(config);

            return services;
        }
    }
}
