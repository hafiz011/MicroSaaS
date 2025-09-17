using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace Microservice.Session.Entities
{
    public class Sessions
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public string Tenant_Id { get; set; }
        public string User_Id { get; set; } //user collection id
        public string Ip_Address { get; set; }

        public DateTime Login_Time { get; set; }
        public DateTime? Logout_Time { get; set; }
        public double Session_Duration { get; set; }
        public int ActionCount { get; set; } = 0;

        // Aggregated product actions
        public List<string> Unique_Products_Viewed { get; set; } = new();
        public List<string> Unique_Products_Added { get; set; } = new();
        public List<string> Unique_Products_Purchased { get; set; } = new();
        public bool Conversion_Flag { get; set; }

        public double Session_RiskScore { get; set; }  // Suspicious behavior score
        public bool isSuspicious { get; set; }
        public string ReferrerUrl { get; set; }

        public DeviceInfo Device { get; set; }
        public Location Geo_Location { get; set; }
        public bool isActive { get; set; }
    }
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
        public string CountryIsoCode { get; set; }
        public string Continent { get; set; }
        public string ContinentCode { get; set; }
        public string City { get; set; }
        public string Region { get; set; }
        public string RegionIsoCode { get; set; }
        public string Latitude_Longitude { get; set; }
        public int? AccuracyRadius { get; set; }
        public string Isp { get; set; }
        public string TimeZone { get; set; }
        public bool isVPN { get; set; }
    }
}
