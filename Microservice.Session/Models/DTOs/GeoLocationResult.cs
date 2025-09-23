namespace Microservice.Session.Models.DTOs
{
    public class GeoLocationResult
    {
        public string Ip { get; set; }

        // Country Info
        public string Country { get; set; }
        public string CountryIsoCode { get; set; }

        // Continent Info
        public string Continent { get; set; }
        public string ContinentCode { get; set; }

        // Region / Subdivision
        public string Region { get; set; }
        public string RegionIsoCode { get; set; }

        // City
        public string City { get; set; }

        // Location
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int? AccuracyRadius { get; set; }
        public string Isp { get; set; }
        public string TimeZone { get; set; }
    }
}
