
using Microservice.Session.Entities;

namespace Microservice.Session.Infrastructure.Interfaces
{
    public interface IUserInfoRepository
    {
        Task CreateUserAsync(Users user);
        Task UpdateUserAsync(Users user);
        //Task<Users> GetUserByIdAsync(string User_Id, string apikeyid);
        Task<Users> getUserById(string userId, string tenantId);
        Task<List<Users>> GetUserBySessionIdListAsync(string tenant_Id, List<string> user_Ids);
    }
}
