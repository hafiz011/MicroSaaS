using Microservice.AuthService.Database;
using Microservice.AuthService.Entities;
using Microservice.AuthService.Infrastructure.Interfaces;
using Microservice.AuthService.Models;
using Microservice.AuthService.Models.DTOs;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Threading.Tasks;

namespace Microservice.AuthService.Infrastructure.Services
{
    public class RabbitMqConsumerService : BackgroundService
    {
        private readonly ISuspiciousActivityRepository _suspiciousRepository;
        private readonly IModel _channel;
        private readonly GrpcServiceClient _grpcServiceClient;
        private readonly IServiceScopeFactory _scopeFactory;




        public RabbitMqConsumerService(ISuspiciousActivityRepository suspiciousRepository,
            IModel channel,
            GrpcServiceClient grpcServiceClient,
            IServiceScopeFactory scopeFactory)
        {
            _suspiciousRepository = suspiciousRepository;
            _channel = channel;
            _grpcServiceClient = grpcServiceClient;
            _scopeFactory = scopeFactory;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _channel.QueueDeclare(queue: "session-risk-check-v2", durable: true, exclusive: false, autoDelete: false);

            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.Received += async (model, ea) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();

                try
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);
                    var message = JsonConvert.DeserializeObject<SessionRiskCheckMessage>(json);

                    if (message == null || string.IsNullOrWhiteSpace(message.SessionId)) return;

                    var prediction = await PredictRisk(message);

                    var suspicious = new SuspiciousActivity
                    {
                        SessionId = message.SessionId,
                        TenantId = message.TenantId,
                        UserId = message.UserId,
                        Email = message.Email,
                        IpAddress = message.Ip_Address,
                        LoginTime = message.Login_Time,
                        RiskScore = prediction.Score,
                        RiskLevel = prediction.Level,
                        RiskFactors = prediction.Factors,
                        DetectedAt = DateTime.UtcNow,
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

                    if (prediction.Level == "High" || prediction.Level == "Medium")
                    {
                        await _suspiciousRepository.InsertAsync(suspicious);
                        await _grpcServiceClient.UpdateSuspiciousSession(suspicious.TenantId, suspicious.SessionId, suspicious.RiskScore, suspicious.IsSuspicious);
                        var emailBody = BuildEmailBody(suspicious, message);
                        bool emailSent = await emailService.SendEmailAsync(message.Email, "A suspicious login detected", emailBody);
                        if (!emailSent)
                            Console.WriteLine($"Failed to send suspicious notification email: {message.Email}");
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message: {ex.Message}");
                }
            };

            _channel.BasicConsume(queue: "session-risk-check-v2", autoAck: true, consumer: consumer);
            return Task.CompletedTask;
        }

        private async Task<RiskPrediction> PredictRisk(SessionRiskCheckMessage message)
        {
            var response = await _grpcServiceClient.SessionListCheck(message.TenantId, message.UserId, message.SessionId, 10);
            var flags = new List<string>();
            double riskScore = 0;

            // Build behavioral profile
            var baselineIPs = response.Select(s => s.IpAddress).Distinct().ToList();
            var baselineCountries = response.Select(s => s.Country).Distinct().ToList();
            var baselineDevices = response.Select(s => s.Fingerprint).Distinct().ToList();
            var baselineHours = response.Select(s => s.LoginTime.ToDateTime().Hour).ToList();

            // Check #1: Location mismatch
            if (!baselineCountries.Contains(message.Geo_Location.Country))
            {
                flags.Add("Country mismatch");
                riskScore += 0.2;
            }

            // Check #2: Device mismatch
            if (!baselineDevices.Contains(message.Device.Fingerprint))
            {
                flags.Add("Device fingerprint mismatch");
                riskScore += 0.3;
            }

            // Check #3: Login time anomaly
            var avgLoginHour = baselineHours.Average();
            var currentHour = message.Login_Time.Hour;
            if (Math.Abs(currentHour - avgLoginHour) >= 6)
            {
                flags.Add("Unusual login hour");
                riskScore += 0.1;
            }

            // Check #4: VPN / ASN anomaly
            if (message.Geo_Location.is_vpn)
            {
                flags.Add("VPN or Proxy detected");
                riskScore += 0.1;
            }

            // Check #5: Impossible travel
            var lastSession = response.OrderByDescending(s => s.LoginTime).FirstOrDefault();
            if (lastSession != null && lastSession.LogoutTime != null)
            {
                var logoutTime = lastSession.LogoutTime.ToDateTime();

                var hoursDiff = (message.Login_Time - logoutTime).TotalHours;

                var latLonParts = message.Geo_Location.Latitude_Longitude?.Split(',');
                var curLat = double.TryParse(latLonParts?[0], out var lat1) ? lat1 : 0;
                var curLon = double.TryParse(latLonParts?[1], out var lon1) ? lon1 : 0;

                var database_latLonParts = lastSession.LatLon?.Split(",");
                var prevLat = double.TryParse(database_latLonParts?[0], out var lat) ? lat : 0;
                var prevLon = double.TryParse(database_latLonParts?[1], out var lon) ? lon : 0;

                var kmDistance = GeoUtils.GetDistanceInKm(curLat, curLon, prevLat, prevLon);
                var requiredSpeed = kmDistance / (hoursDiff == 0 ? 0.1 : hoursDiff);

                if (requiredSpeed > 1000)
                {
                    flags.Add("Impossible travel detected");
                    riskScore += 0.3;
                }
            }

            // Decision
            var riskLevel = riskScore switch
            {
                >= 0.5 => "High",
                >= 0.3 => "Medium",
                _ => "Low"
            };
            var isSuspicious = riskScore >= 0.4;
            return new RiskPrediction
            {
                Score = Math.Min(riskScore, 1.0),
                Level = riskLevel,
                Factors = flags
            };
        }


        private string BuildEmailBody(SuspiciousActivity session, SessionRiskCheckMessage message)
        {
            var flags = string.Join("", session.RiskFactors.Select(f => $"<li>{f}</li>"));

            return $"""
            <html>
            <head></head>
            <body style="font-family: Arial, sans-serif; background-color: #f9f9f9; padding: 20px; color: #333;">
                <div style="max-width: 600px; margin: auto; background: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); padding: 20px;">
                    <div style="background-color: #ff4d4f; color: white; padding: 15px; border-radius: 8px 8px 0 0; text-align: center; font-size: 20px; font-weight: bold;">
                        ⚠ Suspicious Login Detected
                    </div>
                    <div style="margin-top: 20px;">
                        <p>Hello,</p>
                        <p>We noticed a suspicious login to your account with the following details:</p>

                        <table style="width:100%; margin-top:10px; line-height: 1.6;">
                            <tr><td><strong>Location:</strong></td><td>{session.Geo_Location?.City}, {session.Geo_Location?.Country}</td></tr>
                            <tr><td><strong>IP Address:</strong></td><td>{session.IpAddress}</td></tr>
                            <tr><td><strong>Time:</strong></td><td>{session.LoginTime}</td></tr>
                            <tr><td><strong>Device:</strong></td><td>{session.Device?.Browser} on {session.Device?.OS}</td></tr>
                        </table>

                        <p><strong>Risk Factors:</strong></p>
                        <ul style="margin-top: 10px; padding-left: 20px;">
                            {flags}
                        </ul>

                        <p>If this wasn't you, please take immediate action to secure your account.</p>
                        <p>
                            <a href="https://{message.Cliend_Domaim}" style="background-color:#ff4d4f; color:white; padding:10px 20px; border-radius:5px; text-decoration:none; display:inline-block; margin-top:10px;">
                                Review Account Activity
                            </a>
                        </p>
                    </div>
                    <div style="margin-top: 30px; font-size: 12px; color: #888; text-align: center;">
                        This is an automated message from Tech Ciph. Do not reply to this email.
                    </div>
                </div>
            </body>
            </html>
            """;
        }


    }

}
