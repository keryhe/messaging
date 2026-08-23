using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Keryhe.Messaging.Channels.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddChannelPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.TryAddSingleton<IChannelRegistry<T>, ChannelRegistry<T>>();
            services.AddSingleton<IMessagePublisher<T>>(sp => new ChannelPublisher<T>(
                sp.GetRequiredService<IOptionsMonitor<ChannelPublisherOptions>>(),
                sp));
            services.Configure<ChannelPublisherOptions>(config);

            return services;
        }

        public static IServiceCollection AddChannelListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.TryAddSingleton<IChannelRegistry<T>, ChannelRegistry<T>>();
            services.AddSingleton<IMessageListener<T>>(sp => new ChannelListener<T>(
                sp.GetRequiredService<IOptionsMonitor<ChannelListenerOptions>>(),
                sp,
                sp.GetRequiredService<ILogger<ChannelListener<T>>>()));
            services.Configure<ChannelListenerOptions>(config);

            return services;
        }
    }
}
