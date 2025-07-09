using Microservice.Session.Entities;

namespace Microservice.Session.Models.DashboardDTOs
{
    public class SuspiciousWithSessionDto
    {
        public SuspiciousActivity Suspicious { get; set; }
        public Sessions SessionDetails { get; set; }
    }
}
