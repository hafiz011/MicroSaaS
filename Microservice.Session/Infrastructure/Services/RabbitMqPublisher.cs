using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Models.DTOs;
using Newtonsoft.Json;
using System.Text;

namespace Microservice.Session.Infrastructure.Services
{
    public class RabbitMqPublisher : IRabbitMqPublisher
    {
        private readonly IModel _channel;

        public RabbitMqPublisher()
        {
            var factory = new ConnectionFactory() { HostName = "localhost" }; // or docker/rabbitmq-server
            var connection = factory.CreateConnection();
            _channel = connection.CreateModel();
            _channel.QueueDeclare(queue: "session-risk-check", durable: false, exclusive: false, autoDelete: false);
        }

        public void PublishSessionRiskCheck(SessionRiskCheckMessage message)
        {
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            _channel.BasicPublish(exchange: "", routingKey: "session-risk-check", basicProperties: null, body: body);
            Console.WriteLine($"[Publisher] Published message for SessionId: {message.SessionId}");
        }
    }
}
