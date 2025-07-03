using Microservice.Session.Entities;
namespace Microservice.Session.Infrastructure.Interfaces
{
    public interface IApiKeyRepository
    {
        Task<Tenants> CreateApiKeyAsync(Tenants key);
        Task<Tenants> GetApiByUserIdAsync(string userId);
        Task<IEnumerable<Tenants>> GetAllApiKey(string apiSecret);
        Task<bool> RenewApiKeyAsync(Tenants update);
        Task<bool> RevokeApiKeyAsync(string userId);
        Task<bool> TrackUsageAsync(string key);
        Task<Tenants> ValidateApiKeyAsync(string key);
        Task<bool> RegenerateApiKeyAsync(Tenants newKey);
        Task<Tenants> GetApiByApiKeyIdAsync(string rawKey);
        Task<Tenants> GetApiKeyIdAsync(string Id);
    }
}
