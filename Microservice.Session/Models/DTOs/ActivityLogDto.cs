namespace Microservice.Session.Models.DTOs
{
    public class ActivityLogDto
    {
        public string Activity_Type { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
        public DateTime LocalTime { get; set; }
    }
}
