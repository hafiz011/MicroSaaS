using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Models.DTOs;
using MongoDB.Driver;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace Microservice.Session.Infrastructure.Services
{
    public class RabbitMqConsumerService : BackgroundService
    {
        private readonly ISuspiciousActivityRepository _suspiciousRepository;
        private readonly ISessionRepository _sessionRepository;
        private readonly IModel _channel;

        public RabbitMqConsumerService(
            ISuspiciousActivityRepository suspiciousRepository,
            ISessionRepository sessionRepository,
            IModel channel)
        {
            _suspiciousRepository = suspiciousRepository;
            _sessionRepository = sessionRepository;
            _channel = channel;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _channel.QueueDeclare(queue: "session-risk-check", durable: false, exclusive: false, autoDelete: false);
            var consumer = new EventingBasicConsumer(_channel);

            consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);
                    var message = JsonConvert.DeserializeObject<SessionRiskCheckMessage>(json);

                    if (message == null || string.IsNullOrWhiteSpace(message.SessionId)) return;

                    var session = await _sessionRepository.GetSessionByIdAsync(message.SessionId);
                    if (session == null) return;

                    var prediction = PredictRisk(session);

                    var suspicious = new SuspiciousActivity
                    {
                        SessionId = session.Id,
                        TenantId = session.Tenant_Id,
                        UserId = session.User_Id,
                        RiskScore = prediction.Score,
                        RiskLevel = prediction.Level,
                        RiskFactors = prediction.Factors,
                        DetectedAt = DateTime.UtcNow,
                        IsSuspicious = prediction.Score > 0.5
                    };

                    await _suspiciousRepository.InsertAsync(suspicious);
                    Console.WriteLine($"[✔] Suspicious inserted for session: {session.Id}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Error processing message: {ex.Message}");
                }
            };

            _channel.BasicConsume(queue: "session-risk-check", autoAck: true, consumer: consumer);
            Console.WriteLine("[*] Listening for suspicious session jobs...");
            return Task.CompletedTask;
        }

        private RiskPrediction PredictRisk(Sessions session)
        {
            var score = 0.0;
            var reasons = new List<string>();

            if (session.Geo_Location?.is_vpn == true)
            {
                score += 0.6;
                reasons.Add("VPN");
            }

            if (session.Device?.Device_Type == "Unknown")
            {
                score += 0.3;
                reasons.Add("Unknown Device");
            }

            var level = score switch
            {
                >= 0.8 => "High",
                >= 0.5 => "Medium",
                _ => "Low"
            };

            return new RiskPrediction
            {
                Score = Math.Min(score, 1.0),
                Level = level,
                Factors = reasons
            };
        }
    }
}
