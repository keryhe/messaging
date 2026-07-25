using Keryhe.Messaging.IO.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Keryhe.Messaging.IO.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddFileSystemListener<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddTransient<IMessageListener<T>, FileSystemListener<T>>();

            // Each source picks its own file type, so both serializers are registered
            // keyed by that type and resolved per-source rather than once at startup.
            services.TryAddKeyedTransient<IFileSerializer<T>, JsonFileSerializer<T>>("json");
            services.TryAddKeyedTransient<IFileSerializer<T>, XmlFileSerializer<T>>("xml");

            services.Configure<FileSystemListenerOptions>(config);
            return services;
        }

        public static IServiceCollection AddFileSystemPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddTransient<IMessagePublisher<T>, FileSystemPublisher<T>>();

            // Each destination picks its own file type, so both serializers are registered
            // keyed by that type and resolved per-send rather than once at startup.
            services.TryAddKeyedTransient<IFileSerializer<T>, JsonFileSerializer<T>>("json");
            services.TryAddKeyedTransient<IFileSerializer<T>, XmlFileSerializer<T>>("xml");

            services.Configure<FileSystemPublisherOptions>(config);

            return services;
        }
    }
}
