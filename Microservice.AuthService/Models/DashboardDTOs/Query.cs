using Microsoft.AspNetCore.Mvc;

namespace Microservice.AuthService.Models.DashboardDTOs
{
    public class Query
    {
        [FromQuery(Name = "from")]
        public DateTime? From { get; set; }

        [FromQuery(Name = "to")]
        public DateTime? To { get; set; }

        [FromQuery(Name = "country")]
        public string? Country { get; set; }

        [FromQuery(Name = "device")]
        public string? Device { get; set; }
    }

}
