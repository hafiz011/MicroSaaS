using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Microservice.Session.Models.DTOs
{
    public class UserActivityLogMessage
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public string Tenant_Id { get; set; }
        public string Session_Id { get; set; }
        public string Activity_Type { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
        public DateTime LocalTime { get; set; }
        public DateTime Time_Stamp { get; set; }
    }
}