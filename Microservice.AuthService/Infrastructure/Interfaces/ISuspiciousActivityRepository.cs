using Microservice.AuthService.Entities;


namespace Microservice.AuthService.Infrastructure.Interfaces
{
    public interface ISuspiciousActivityRepository
    {
        Task InsertAsync(SuspiciousActivity activity);
        Task<List<SuspiciousActivity>> GetByTenantAsync(string tenantId, DateTime? from = null, DateTime? to = null);
        Task<List<SuspiciousWithSessionDto>> GetSuspiciousWithSessionDetailsAsync(string tenantId, DateTime? from = null, DateTime? to = null);
    }
}
