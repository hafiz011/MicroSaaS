namespace Microservice.Session.Models.DashboardDTOs
{
    public class SuspiciousActivityDto
    {
        public string User_Id { get; set; }
        public string Ip_Address { get; set; }
        public string Flags { get; set; }
        public string Device { get; set; }
        public string Location { get; set; }
        public string Timestamp { get; set; }
    }
}
