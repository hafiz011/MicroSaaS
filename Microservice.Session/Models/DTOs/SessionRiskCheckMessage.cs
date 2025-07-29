using MongoDB.Bson.Serialization.Attributes;

namespace Microservice.Session.Models.DTOs
{
    public class SessionRiskCheckMessage
    {
        public string TenantId { get; set; }
        public string SessionId { get; set; }
        public string UserId { get; set; }
        public string Email { get; set; }
        public string Ip_Address { get; set; }
        public DateTime Local_Time { get; set; }
        public DateTime Login_Time { get; set; } = DateTime.UtcNow;
        public DeviceInfo Device { get; set; }
        public Location Geo_Location { get; set; }
        public string Cliend_Domaim { get; set; }

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
        public string City { get; set; }
        public string Region { get; set; }
        public string Postal { get; set; }
        public string Latitude_Longitude { get; set; }
        public string Isp { get; set; }
        public string TimeZone { get; set; }
        public bool is_vpn { get; set; }
    }





}

