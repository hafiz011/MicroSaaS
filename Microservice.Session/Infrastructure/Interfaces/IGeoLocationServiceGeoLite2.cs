using Microservice.Session.Models.DTOs;

namespace Microservice.Session.Infrastructure.Interfaces
{
    public interface IGeoLocationServiceGeoLite2
    {
        Task<GeoLocationResult?> GetGeoLocation(string ipAddress);

    }
}
