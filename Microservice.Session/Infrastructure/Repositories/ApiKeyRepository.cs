using Microservice.Session.Controllers;
using Microservice.Session.Infrastructure.MongoDb;
using Microservice.Session.Infrastructure.Services;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using MongoDB.Driver;


namespace Microservice.Session.Infrastructure.Repositories
{
    public class ApiKeyRepository : IApiKeyRepository
    {
        private readonly IMongoCollection<Tenants> _collection;
        public ApiKeyRepository(MongoDbContext context )
        {
            _collection = context.ApiKey;
        }
        public async Task<Tenants> CreateApiKeyAsync(Tenants apiKey)
        {
            await _collection.InsertOneAsync(apiKey);
            return apiKey;
        }

        public async Task<IEnumerable<Tenants>> GetAllApiKey(string apiSecret)
        {
            var sort = Builders<Tenants>.Sort.Ascending(x => x.Created_At);
            return await _collection.Find(_ => true).Sort(sort).ToListAsync();
        }

        public async Task<Tenants> GetApiKeyIdAsync(string id)
        {
            return await _collection.Find(a => a.Id == id).FirstOrDefaultAsync();
        }

        public async Task<Tenants> GetApiByApiKeyIdAsync(string rawKey)
        {
            var hashedKey = ApiKeyGenerator.HashApiKey(rawKey);
            return await _collection.Find(a => a.ApiSecret == hashedKey).FirstOrDefaultAsync();
        }

        public async Task<Tenants> GetApiByUserIdAsync(string userId)
        {
            return await _collection.Find(a => a.UserId == userId).FirstOrDefaultAsync();
        }

        public async Task<bool> RegenerateApiKeyAsync(Tenants newKey)
        {
            var filter = Builders<Tenants>.Filter.Where(a => a.UserId == newKey.UserId && !a.IsRevoked);
            var update = Builders<Tenants>.Update.Set(a => a.ApiSecret, newKey.ApiSecret);

            var result = await _collection.UpdateOneAsync(filter, update);

            return result.ModifiedCount > 0;
        }

        public async Task<bool> RenewApiKeyAsync(Tenants update)
        {
            var filter = Builders<Tenants>.Filter.Where(a => a.UserId == update.UserId && !a.IsRevoked);
            var updateDef = Builders<Tenants>.Update
                .Set(a => a.Plan, update.Plan)
                .Set(a => a.ExpirationDate, update.ExpirationDate)
                .Set(a => a.RequestLimit, update.RequestLimit)
                .Set(a => a.IsRevoked, update.IsRevoked);

            var result = await _collection.UpdateOneAsync(filter, updateDef);

            return result.ModifiedCount > 0;
        }

        public async Task<bool> RevokeApiKeyAsync(string userId)
        {
            var update = Builders<Tenants>.Update.Set(a => a.IsRevoked, true);
            var result = await _collection.UpdateOneAsync(a => a.UserId == userId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> TrackUsageAsync(string rawKey)
        {
            var hashedKey = ApiKeyGenerator.HashApiKey(rawKey);

            var filter = Builders<Tenants>.Filter.And(
                Builders<Tenants>.Filter.Eq(a => a.ApiSecret, hashedKey),
                Builders<Tenants>.Filter.Gt(a => a.RequestLimit, 0),
                Builders<Tenants>.Filter.Gt(a => a.ExpirationDate, DateTime.UtcNow),
                Builders<Tenants>.Filter.Eq(a => a.IsRevoked, false)
            );

            var update = Builders<Tenants>.Update.Inc(a => a.RequestLimit, -1);

            var result = await _collection.UpdateOneAsync(filter, update);

            return result.ModifiedCount > 0;
        }

        public async Task<Tenants> ValidateApiKeyAsync(string rawKey)
        {
            var hashedKey = ApiKeyGenerator.HashApiKey(rawKey);
            return await _collection.Find(a =>
                a.ApiSecret == hashedKey &&
                a.ExpirationDate > DateTime.UtcNow &&
                !a.IsRevoked).FirstOrDefaultAsync();
        }
    }
}
