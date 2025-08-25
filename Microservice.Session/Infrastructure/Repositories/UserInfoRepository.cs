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

        public async Task CreateUserAsync(Users user)
        {
            await _collection.InsertOneAsync(user);
        }

        public async Task UpdateUserAsync(Users user)
        {
            var filter = Builders<Users>.Filter.Eq(u => u.User_Id, user.User_Id);
            var update = Builders<Users>.Update
                .Set(u => u.Last_login, DateTime.UtcNow)
                .Set(u => u.Name, user.Name)
                .Set(u => u.Email, user.Email);

            await _collection.UpdateOneAsync(filter, update);
        }

        // get user info
        public async Task<Users> getUserById(string userId, string tenantId)
        {
            var filter = Builders<Users>.Filter.Where(a => a.User_Id == userId && a.Tenant_Id == tenantId);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        // active users list
        public async Task<List<Users>> GetUserBySessionIdListAsync(string tenantId, List<string> userIds)
        {
            if (userIds == null || !userIds.Any())
                return new List<Users>();

            var filterBuilder = Builders<Users>.Filter;

            var tenantFilter = filterBuilder.Eq(u => u.Tenant_Id, tenantId);

            var userFilter = filterBuilder.In(u => u.User_Id, userIds);

            var filter = filterBuilder.And(tenantFilter, userFilter);

            return await _collection.Find(filter).ToListAsync();
        }



    }
}
