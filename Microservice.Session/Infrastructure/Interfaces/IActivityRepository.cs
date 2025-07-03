using Microservice.Session.Entities;
using Microservice.Session.Models.DTOs;

namespace Microservice.Session.Infrastructure.Interfaces
{
    public interface IActivityRepository
    {
        Task CreateLogActivityAsync(ActivityLog log);
        Task<IEnumerable<ActivityLog>> GetUserActivitiesAsync(string id);
    }
}
