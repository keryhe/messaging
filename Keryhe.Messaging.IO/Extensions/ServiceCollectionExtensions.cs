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

            string fileType =  (string)config.GetValue(typeof(string), "FileType");
            switch(fileType)
            {
                case "json":
                    services.TryAddTransient<IFileSerializer<T>, JsonFileSerializer<T>>();
                    break;
                case "xml":
                    services.TryAddTransient<IFileSerializer<T>, XmlFileSerializer<T>>();
                    break;
            }

            services.Configure<FileSystemListenerOptions>(config);
            return services;
        }

        public static IServiceCollection AddFileSystemPublisher<T>(this IServiceCollection services, IConfiguration config)
        {
            services.AddTransient<IMessagePublisher<T>, FileSystemPublisher<T>>();

            string fileType = (string)config.GetValue(typeof(string), "FileType");
            switch (fileType)
            {
                case "json":
                    services.TryAddTransient<IFileSerializer<T>, JsonFileSerializer<T>>();
                    break;
                case "xml":
                    services.TryAddTransient<IFileSerializer<T>, XmlFileSerializer<T>>();
                    break;
            }

            services.Configure<FileSystemPublisherOptions>(config);

            return services;
        }
    }
}
