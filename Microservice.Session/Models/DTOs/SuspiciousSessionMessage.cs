namespace Microservice.Session.Models.DTOs
{
    public class SuspiciousSessionMessage
    {
        public string SessionId { get; set; }
        public string TenantId { get; set; }
        public string UserId { get; set; }
    }
}
