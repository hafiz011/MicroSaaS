namespace Microservice.Session.Models.DTOs
{
    public class SessionRequestDto
    {
        public string User_Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Ip_Address { get; set; }
        public DeviceInfoDto Device { get; set; }
        public DateTime LocalTime { get; set; }
    }

    public class DeviceInfoDto
    {
        public string Fingerprint { get; set; }
        public string Browser { get; set; }
        public string Device_Type { get; set; }
        public string OS { get; set; }
        public string Language { get; set; }
        public string Screen_Resolution { get; set; }
    }

}
