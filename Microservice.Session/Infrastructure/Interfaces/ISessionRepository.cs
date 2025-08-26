using Microservice.Session.Models.DTOs;
using Microservice.Session.Entities;
using Microsoft.Extensions.Primitives;

namespace Microservice.Session.Infrastructure.Interfaces
{
    public interface ISessionRepository
    {
        Task<Sessions> CreateSessionAsync(Sessions sessions);
        Task EndSessionAsync(string sessionId);
        Task<Sessions> GetSessionByIdAsync(string existingSessionId, string tenantId);
        Task UpdateSessionAsync(Sessions update);


        //Task<List<Sessions>> GetRecentSessionsForUserAsync(string Tenant_Id, string userId, int v);
        //Task<IEnumerable<Sessions>> GetRecentUndetectedSessionsAsync();
        //Task UpdateSuspiciousSessionAsync(string id, Sessions sessions);
        Task<List<Sessions>> ActiveSessionList(string tenantId, DateTime? from, DateTime? to, string device, string country);
        Task<List<Sessions>> GetSessionCheckListAsync(string tenantId, string userId, string SessionId, int v);
        Task<List<Sessions>> GetSessionsAnalytics(string tenantId, DateTime? from, DateTime? to, string device, string country);
    }
}
