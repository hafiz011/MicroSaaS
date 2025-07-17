
using Microservice.Session.Entities;

namespace Microservice.Session.Infrastructure.Interfaces
{
    public interface IUserInfoRepository
    {
        Task CreateUserAsync(Users user);
        Task UpdateUserAsync(Users user);
        Task<Users> GetUserByIdAsync(string User_Id);
        Task<Users> getUserById(string userId, string tenantId);
    }
}
