namespace Microservice.AuthService.Models.DashboardDTOs
{
    public class Query
    {
        public string? Country { get; set; }     // Optional (nullable)
        public string? Device { get; set; }      // Optional (nullable)
        public DateTime? From { get; set; }      // Optional
        public DateTime? To { get; set; }        // Optional
        public string? Range { get; set; }       // Optional
    }
}
