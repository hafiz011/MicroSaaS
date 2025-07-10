namespace Microservice.AuthService.Models.DTOs
{
    public class RiskPrediction
    {
        public double Score { get; set; } // 0.0 – 1.0
        public string Level { get; set; } // "Low", "Medium", "High"
        public List<string> Factors { get; set; } // ["New Device", "Unusual IP"]
    }
}
