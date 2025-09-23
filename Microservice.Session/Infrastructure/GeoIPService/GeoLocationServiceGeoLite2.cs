using MaxMind.GeoIP2;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Models.DTOs;
using Microsoft.Extensions.Logging;

namespace Microservice.Session.Infrastructure.GeoIPService
{
    public class GeoLocationServiceGeoLite2 : IGeoLocationServiceGeoLite2
    {
        private readonly DatabaseReader _cityReader;
        private readonly ILogger<GeoLocationServiceGeoLite2> _logger;

        public GeoLocationServiceGeoLite2(string cityDbPath, ILogger<GeoLocationServiceGeoLite2> logger)
        {
            _logger = logger;
            _cityReader = new DatabaseReader(cityDbPath); // synchronous reader
        }
        public Task<GeoLocationResult?> GetGeoLocation(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return Task.FromResult<GeoLocationResult?>(null);

            try
            {
                var city = _cityReader.City(ip); // synchronous call

                if (string.IsNullOrWhiteSpace(city?.Country?.Name) &&
                    string.IsNullOrWhiteSpace(city?.City?.Name) &&
                    city?.Location?.Latitude == null &&
                    city?.Location?.Longitude == null)
                {
                    return Task.FromResult<GeoLocationResult?>(null);
                }
                var result = new GeoLocationResult
                {
                    Ip = ip,
                    Country = city.Country?.Name,
                    CountryIsoCode = city.Country?.IsoCode,
                    Continent = city.Continent?.Name,
                    ContinentCode = city.Continent?.Code,
                    Region = city.MostSpecificSubdivision?.Name,
                    RegionIsoCode = city.MostSpecificSubdivision?.IsoCode,
                    City = city.City?.Name,
                    Latitude = city.Location?.Latitude ?? 0,
                    Longitude = city.Location?.Longitude ?? 0,
                    AccuracyRadius = city.Location?.AccuracyRadius ?? 0,
                    Isp = null, // Not available in GeoLite2 City database
                    TimeZone = city.Location?.TimeZone
                };
                return Task.FromResult<GeoLocationResult?>(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GeoIP lookup failed for {ip}");
                return Task.FromResult<GeoLocationResult?>(null);
            }
        }
    }
}
