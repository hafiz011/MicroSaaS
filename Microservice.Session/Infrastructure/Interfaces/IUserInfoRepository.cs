
using Microservice.Session.Entities;

namespace Microservice.Session.Infrastructure.Interfaces
{
    public interface IUserInfoRepository
    {
        Task<Users> getUserById(string userId, string tenantId);
    }
}
