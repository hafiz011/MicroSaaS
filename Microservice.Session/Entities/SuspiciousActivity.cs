using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Microservice.Session.Entities
{
    public class SuspiciousActivity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("Tenant_Id")]
        public string TenantId { get; set; }

        [BsonElement("Session_Id")]
        public string SessionId { get; set; }

        [BsonElement("User_Id")]
        public string UserId { get; set; }

        [BsonElement("Risk_Score")]
        public double RiskScore { get; set; }

        [BsonElement("Risk_Level")]
        public string RiskLevel { get; set; }

        [BsonElement("Detected_At")]
        public DateTime DetectedAt { get; set; }

        [BsonElement("Risk_Factors")]
        public List<string> RiskFactors { get; set; }

        [BsonElement("Is_Suspicious")]
        public bool IsSuspicious { get; set; }
    }
}
