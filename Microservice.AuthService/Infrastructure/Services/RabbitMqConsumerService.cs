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

            foreach (var session in message)
            {
                var recentSessions = await _sessionCollection
                    .Find(s => s.UserId == session.UserId && s.Id != session.Id)
                    .SortByDescending(s => s.LoginTime)
                    .Limit(5)
                    .ToListAsync();

                if (recentSessions.Count == 0) continue;

                var flags = new List<string>();
                double riskScore = 0;

                // Build behavioral profile
                var baselineIPs = recentSessions.Select(s => s.Ip_Address).Distinct().ToList();
                var baselineCountries = recentSessions.Select(s => s.Geo_Location?.Country).Distinct().ToList();
                var baselineDevices = recentSessions.Select(s => s.Device?.Fingerprint).Distinct().ToList();
                var baselineHours = recentSessions.Select(s => s.Local_Time.Hour).ToList();

                // Check #1: IP mismatch
                if (!baselineIPs.Contains(session.Ip_Address))
                {
                    flags.Add("IP address mismatch");
                    riskScore += 0.2;
                }

                // Check #2: Location mismatch
                if (!baselineCountries.Contains(session.Geo_Location?.Country))
                {
                    flags.Add("Country mismatch");
                    riskScore += 0.2;
                }

                // Check #3: Device mismatch
                if (!baselineDevices.Contains(session.Device?.Fingerprint))
                {
                    flags.Add("Device fingerprint mismatch");
                    riskScore += 0.2;
                }

                // Check #4: Login time anomaly
                var avgLoginHour = baselineHours.Average();
                var currentHour = session.Local_Time.Hour;
                if (Math.Abs(currentHour - avgLoginHour) >= 6)
                {
                    flags.Add("Unusual login hour");
                    riskScore += 0.15;
                }

                // Check #5: VPN / ASN anomaly
                var isVpn = session.Geo_Location?.IsVpn ?? false;
                if (isVpn)
                {
                    flags.Add("VPN or Proxy detected");
                    riskScore += 0.15;
                }

                // Check #6: Impossible travel (velocity)
                var lastSession = recentSessions.FirstOrDefault();
                if (lastSession != null)
                {
                    var hoursDiff = (session.LoginTime - lastSession.LogoutTime)?.TotalHours ?? 0;

                    var kmDistance = GeoUtils.GetDistanceInKm(
                        session.Geo_Location?.Latitude ?? 0,
                        session.Geo_Location?.Longitude ?? 0,
                        lastSession.Geo_Location?.Latitude ?? 0,
                        lastSession.Geo_Location?.Longitude ?? 0
                    );

                    var requiredSpeed = kmDistance / (hoursDiff == 0 ? 0.1 : hoursDiff);
                    if (requiredSpeed > 1000) // e.g., > 1000 km/h = impossible
                    {
                        flags.Add("Impossible travel detected");
                        riskScore += 0.3;
                    }
                }

                // Decision
                var riskLevel = riskScore switch
                {
                    >= 0.6 => "High",
                    >= 0.4 => "Medium",
                    _ => "Low"
                };

                var isSuspicious = riskScore >= 0.4;















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


        



        private string BuildEmailBody(Session session, List<string> flags)
        {
            return $"""
        A suspicious login was detected for your account.

        Location: {session.Geo_Location?.City}, {session.Geo_Location?.Country}
        IP Address: {session.Ip_Address}
        Time: {session.Local_Time}
        Device: {session.Device?.Browser} on {session.Device?.OS}
        Flags:
        {string.Join("\n", flags.Select(f => $"- {f}"))}
        """;
        }
    

    }

}
