using MaxMind.GeoIP2;
using Microservice.Session.Models.DTOs;
using Microsoft.Extensions.Logging;

namespace Microservice.Session.Infrastructure.GeoIPService
{
    public class GeoLocationServiceGeoLite2 : IGeoLocationService
    {
        private readonly DatabaseReader _cityReader;
        private readonly ILogger<GeoLocationServiceGeoLite2> _logger;

        public GeoLocationServiceGeoLite2(string cityDbPath, ILogger<GeoLocationServiceGeoLite2> logger)
        {
            _logger = logger;
            _cityReader = new DatabaseReader(cityDbPath); // synchronous reader
        }

        public GeoLocationResult? GetGeoLocation(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return null;

            try
            {
                var city = _cityReader.City(ip); // synchronous

                return new GeoLocationResult
                {
                    Ip = ip,
                    Country = city.Country.Name,
                    CountryIsoCode = city.Country.IsoCode,
                    Continent = city.Continent.Name,
                    ContinentCode = city.Continent.Code,
                    Region = city.MostSpecificSubdivision?.Name,
                    RegionIsoCode = city.MostSpecificSubdivision?.IsoCode,
                    City = city.City.Name,
                    Latitude = city.Location.Latitude,
                    Longitude = city.Location.Longitude,
                    AccuracyRadius = city.Location.AccuracyRadius,
                    TimeZone = city.Location.TimeZone
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GeoIP lookup failed for {ip}");
                return null;
            }
        }
    }

    public interface IGeoLocationService
    {
        GeoLocationResult? GetGeoLocation(string ip);
    }
}
