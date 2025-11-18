using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Microservice.Session.Entities
{
    public class EventLog
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public string SessionId { get; set; }
        public string TenantId { get; set; }
        public string EventName { get; set; }
        public BsonDocument Data { get; set; }   // flexible payload
        public string Url { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        // optional: processed flags, riskScore
    }
}
