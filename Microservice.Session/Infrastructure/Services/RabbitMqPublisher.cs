using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Models.DTOs;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;

namespace Microservice.Session.Infrastructure.Services
{
    public class RabbitMqPublisher : IRabbitMqPublisher, IDisposable
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;

        // Queue names
        private readonly string _sessionRiskCheckQueue = "session-risk-check-v3";
        private readonly string _userActivityLogQueue = "user-activity-log";

        public RabbitMqPublisher()
        {
            var factory = new ConnectionFactory
            {
                // HostName = "31.97.203.233",
                 HostName = "rabbitmq",
                 UserName = "StrongPassword123",
                 Password = "StrongPassword123"
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // two Queue declare
            _channel.QueueDeclare(
                queue: _sessionRiskCheckQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            _channel.QueueDeclare(
                queue: _userActivityLogQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );
        }

        // Session Risk Check message publish method
        public void PublishSessionRiskCheck(SessionRiskCheckMessage message)
        {
            var json = JsonConvert.SerializeObject(message);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;

            _channel.BasicPublish(
                exchange: "",
                routingKey: _sessionRiskCheckQueue,
                basicProperties: properties,
                body: body
            );

            Console.WriteLine($"[Publisher] Published SessionRiskCheck message for SessionId: {message.SessionId}");
        }

        // User Activity Log message publish method
        public void PublishUserActivityLog(UserActivityLogMessage message)
        {
            var json = JsonConvert.SerializeObject(message);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;

            _channel.BasicPublish(
                exchange: "",
                routingKey: _userActivityLogQueue,
                basicProperties: properties,
                body: body
            );

            Console.WriteLine($"[Publisher] Published UserActivityLog message for session: {message.Session_Id}");
        }

        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}
