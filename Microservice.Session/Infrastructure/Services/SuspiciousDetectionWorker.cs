using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Models.DTOs;
using MongoDB.Driver;

namespace Microservice.Session.Infrastructure.Services
{
    public class SuspiciousDetectionWorker
    {
        private readonly IMongoCollection<Sessions> _sessionCollection;
        private readonly ISuspiciousActivityRepository _suspiciousRepository;
        private readonly IModel _channel;

        public SuspiciousDetectionWorker(IMongoDatabase db, ISuspiciousActivityRepository repository, IModel channel)
        {
            _sessionCollection = db.GetCollection<Sessions>("Sessions");
            _suspiciousRepository = repository;
            _channel = channel;
        }

        public void Start()
        {
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                var message = JsonConvert.DeserializeObject<SessionRiskCheckMessage>(json);

                Console.WriteLine($"[Worker] Processing session: {message.SessionId}");

                var session = await _sessionCollection.Find(x => x.Id == message.SessionId).FirstOrDefaultAsync();
                if (session == null) return;

                //  Call AI/Rule-Based Logic here:
                var prediction = PredictRisk(session);

                // Create suspicious entry:
                var suspicious = new SuspiciousActivity
                {
                    TenantId = message.TenantId,
                    SessionId = session.Id,
                    UserId = session.User_Id,
                    RiskScore = prediction.Score,
                    RiskLevel = prediction.Level,
                    RiskFactors = prediction.Factors,
                    DetectedAt = DateTime.UtcNow,
                    IsSuspicious = prediction.Score > 0.5
                };

                await _suspiciousRepository.InsertAsync(suspicious);
                Console.WriteLine($"[Worker] Saved suspicious activity for Session {session.Id}");
            };

            _channel.BasicConsume(queue: "session-risk-check", autoAck: true, consumer: consumer);
            Console.WriteLine("[Worker] Listening for session-risk-check jobs...");
        }

        // Replace with real ML or rule-based model
        private RiskPrediction PredictRisk(Sessions session)
        {
            var score = 0.0;
            var reasons = new List<string>();

            if (session.Geo_Location?.is_vpn == true)
            {
                score += 0.6;
                reasons.Add("VPN Detected");
            }
            if (session.Device?.Device_Type == "Unknown")
            {
                score += 0.3;
                reasons.Add("Unrecognized Device");
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
