using Microservice.AuthService.Entities;


namespace Microservice.AuthService.Infrastructure.Interfaces
{
    public interface ISuspiciousActivityRepository
    {
        Task InsertAsync(SuspiciousActivity activity);
        Task<List<SuspiciousActivity>> GetByTenantAsync(string tenantId, DateTime? from, DateTime? to, string? device, string? country);

        Task UpdateSuspiciousStatusAsync(string tenantId, string sessionId);
        Task<SuspiciousActivity> GetBySessionIdAsync(string tenantId, string sessionId);
        // Task<List<SuspiciousWithSessionDto>> GetSuspiciousWithSessionDetailsAsync(string tenantId, DateTime? from = null, DateTime? to = null);
    }
}
