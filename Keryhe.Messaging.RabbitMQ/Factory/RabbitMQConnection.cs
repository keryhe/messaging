using System;
using RabbitMQ.Client;
using Keryhe.Messaging.RabbitMQ;
using System.Threading.Tasks;

namespace Keryhe.Messaging.RabbitMQ.Factory;

public interface IRabbitMQConnection : IAsyncDisposable
{
    Task<IConnection> GetConnectionAsync();
}

public class RabbitMQConnection : IRabbitMQConnection
{
    private readonly ConnectionFactory _factory;
    private IConnection _connection;

    public RabbitMQConnection(RabbitMQFactoryOptions options)
    {
        _factory = new ConnectionFactory()
        {
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            HostName = options.HostName,
            Port = options.Port
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
        }
    }

    public async Task<IConnection> GetConnectionAsync()
    {
        if(_connection == null)
        {
            _connection = await _factory.CreateConnectionAsync();
        }

        return _connection;
    }
}
