namespace Microservice.AuthService.Models.DashboardDTOs
{
    public class Query
    {
        public string Country { get; set; }
        public string Device { get; set; }
        public DateTime? From { get; set; }     // Optional override
        public DateTime? To { get; set; }       // Optional override
        public string Range { get; set; }       // "24h", "7d", "30d"
    }
}
