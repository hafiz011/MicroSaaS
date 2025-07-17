using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Infrastructure.MongoDb;
using MongoDB.Driver;

namespace Microservice.Session.Infrastructure.Repositories
{
    public class UserInfoRepository : IUserInfoRepository
    {
        private readonly IMongoCollection<Users> _collection;
        public UserInfoRepository(MongoDbContext context) 
        {
            _collection = context.UserDB;
        }

        // get user info
        public async Task<Users> getUserById(string userId, string tenantId)
        {
            var filter = Builders<Users>.Filter.Where(a => a.User_Id == userId && a.Tenant_Id == tenantId);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }
    }
}
