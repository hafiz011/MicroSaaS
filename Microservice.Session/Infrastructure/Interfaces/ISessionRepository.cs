using Microservice.Session.Models.DTOs;
using Microservice.Session.Entities;
using Microsoft.Extensions.Primitives;

namespace Microservice.Session.Infrastructure.Interfaces
{
    public interface ISessionRepository
    {
        Task<Sessions> CreateSessionAsync(Sessions sessions);
        Task EndSessionAsync(string sessionId);
        Task<Sessions> GetSessionByIdAsync(string existingSessionId);
        Task UpdateSessionAsync(string id, Sessions update);


        Task<List<Sessions>> GetRecentSessionsForUserAsync(string Tenant_Id, string userId, int v);
        Task<IEnumerable<Sessions>> GetRecentUndetectedSessionsAsync();
        Task UpdateSuspiciousSessionAsync(string id, Sessions sessions);
    }
}
