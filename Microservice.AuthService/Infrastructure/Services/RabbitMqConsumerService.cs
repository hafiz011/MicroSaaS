using Microservice.AuthService.Entities;
using Microservice.AuthService.Infrastructure.Interfaces;
using Microservice.AuthService.Models.DTOs;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace Microservice.AuthService.Infrastructure.Services
{
    public class RabbitMqConsumerService : BackgroundService
    {
        private readonly ISuspiciousActivityRepository _suspiciousRepository;
        private readonly IModel _channel;

        public RabbitMqConsumerService(ISuspiciousActivityRepository suspiciousRepository, IModel channel)
        {
            _suspiciousRepository = suspiciousRepository;
            _channel = channel;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _channel.QueueDeclare(queue: "session-risk-check-v2", durable: true, exclusive: false, autoDelete: false);
            
            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);
                    var message = JsonConvert.DeserializeObject<SessionRiskCheckMessage>(json);

                    if (message == null || string.IsNullOrWhiteSpace(message.SessionId)) return;

                    var prediction = PredictRisk(message);

                    var suspicious = new SuspiciousActivity
                    {
                        SessionId = message.SessionId,
                        TenantId = message.TenantId,
                        UserId = message.UserId,
                        IpAddress = message.Ip_Address,
                        LocalTime = message.Local_Time,
                        LoginTime = message.Local_Time,
                        RiskScore = prediction.Score,
                        RiskLevel = prediction.Level,
                        RiskFactors = prediction.Factors,
                        DetectedAt = DateTime.UtcNow,
                        SuspiciousScore = prediction.Score > 0.5,
                        IsSuspicious = true,
                        Device = new SuspiciousActivity.DeviceInfo
                        {
                            Fingerprint = message.Device.Fingerprint,
                            Browser = message.Device.Browser,
                            Device_Type = message.Device.Device_Type,
                            OS = message.Device.OS,
                            Language = message.Device.Language,
                            Screen_Resolution = message.Device.Screen_Resolution
                        },
                        Geo_Location = new SuspiciousActivity.Location
                        {
                            Country = message.Geo_Location.Country,
                            City = message.Geo_Location.City,
                            Region = message.Geo_Location.Region,
                            Postal = message.Geo_Location.Postal,
                            Latitude_Longitude = message.Geo_Location.Latitude_Longitude,
                            Isp = message.Geo_Location.Isp,
                            TimeZone = message.Geo_Location.TimeZone,
                            is_vpn = message.Geo_Location.is_vpn
                        }
                    };

                    await _suspiciousRepository.InsertAsync(suspicious);
                    Console.WriteLine($"Suspicious inserted for session: {message.SessionId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message: {ex.Message}");
                }
            };

            _channel.BasicConsume(queue: "session-risk-check-v2", autoAck: true, consumer: consumer);
            Console.WriteLine("Listening for suspicious session jobs...");
            return Task.CompletedTask;
        }

        private RiskPrediction PredictRisk(SessionRiskCheckMessage message)
        {
            double score = 0;
            var reasons = new List<string>();

            if (message.Geo_Location?.is_vpn == true)
            {
                score += 0.6;
                reasons.Add("VPN");
            }

            if (message.Device?.Device_Type == "Unknown")
            {
                score += 0.3;
                reasons.Add("Unknown Device");
            }

            string level = score switch
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
