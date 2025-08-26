using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Microservice.AuthService.Entities
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

        [BsonElement("User_Email")]
        public string Email { get; set; }

        [BsonElement("IP_Address")]
        public string IpAddress { get; set; }

        [BsonElement("Login_Time")]
        public DateTime LoginTime { get; set; }

        [BsonElement("Risk_Score")]
        public double RiskScore { get; set; }

        [BsonElement("Risk_Level")]
        public string RiskLevel { get; set; }

        [BsonElement("Risk_Factors")]
        public List<string> RiskFactors { get; set; }

        [BsonElement("Is_Suspicious")]
        public bool IsSuspicious { get; set; }

        [BsonElement("Device_Info")]
        public DeviceInfo Device { get; set; }

        [BsonElement("Geo_Location")]
        public Location Geo_Location { get; set; }

        [BsonElement("Detected_At")]
        public DateTime DetectedAt { get; set; }

        [BsonElement("Update_At")]
        public DateTime UpdatedAt { get; set; }

        public class DeviceInfo
        {
            public string Fingerprint { get; set; }
            public string Browser { get; set; }
            public string Device_Type { get; set; }
            public string OS { get; set; }
            public string Language { get; set; }
            public string Screen_Resolution { get; set; }
        }
        public class Location
        {
            public string Country { get; set; }
            public string City { get; set; }
            public string Region { get; set; }
            public string Postal { get; set; }
            public string Latitude_Longitude { get; set; }
            public string Isp { get; set; }
            public string TimeZone { get; set; }
            public bool is_vpn { get; set; }
        }

    }
}
