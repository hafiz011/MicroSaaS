using Microservice.Session.Infrastructure.MongoDb;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microsoft.Extensions.Primitives;
using MongoDB.Driver;

namespace Microservice.Session.Infrastructure.Repositories
{
    public class SessionRepository : ISessionRepository
    {
        private readonly IMongoCollection<Sessions> _collection;
        public SessionRepository(MongoDbContext context) 
        {
            _collection = context.SessionsDB;
        }

        public async Task<Sessions> CreateSessionAsync(Sessions sessions)
        {
            await _collection.InsertOneAsync(sessions);
            return sessions;
        }

        public async Task EndSessionAsync(string sessionId)
        {
            var update = Builders<Sessions>.Update
                .Set(a => a.Logout_Time, DateTime.UtcNow)
                .Set(i => i.isActive, false);
            await _collection.UpdateOneAsync(s => s.Id == sessionId, update);
        }

        public async Task<Sessions> GetSessionByIdAsync(string existingSessionId)
        {
            return await _collection.Find(a => a.Id == existingSessionId).FirstOrDefaultAsync();
        }

        public async Task UpdateSessionAsync(string id, Sessions update)
        {
            var filter = Builders<Sessions>.Filter.Eq(u => u.Id, id);

            var updateDef = Builders<Sessions>.Update
                .Set(s => s.User_Id, update.User_Id)
                .Set(s => s.Ip_Address, update.Ip_Address)
                .Set(s => s.Local_Time, update.Local_Time)
                .Set(s => s.isActive, update.isActive)
                .Set(s => s.Geo_Location, update.Geo_Location)
                .Set(s => s.Device, update.Device);

            await _collection.UpdateOneAsync(filter, updateDef);
        }

        public async Task<IEnumerable<Sessions>> GetRecentUndetectedSessionsAsync()
        {
            var filter = Builders<Sessions>.Filter.Eq(s => s.IsAnalyzed, false);
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<List<Sessions>> GetRecentSessionsForUserAsync(string tenantId, string userId, int limit)
        {
            var filter = Builders<Sessions>.Filter.And(
                Builders<Sessions>.Filter.Eq(s => s.Tenant_Id, tenantId),
                Builders<Sessions>.Filter.Eq(s => s.User_Id, userId),
                Builders<Sessions>.Filter.Eq(s => s.isSuspicious, false)
            );

            return await _collection.Find(filter)
                .SortByDescending(s => s.Login_Time)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task UpdateSuspiciousSessionAsync(string id, Sessions session)
        {
            var filter = Builders<Sessions>.Filter.Eq(s => s.Id, id);

            var update = Builders<Sessions>.Update
                .Set(s => s.IsAnalyzed, session.IsAnalyzed)
                .Set(s => s.isSuspicious, session.isSuspicious)
                .Set(s => s.Suspicious_Flags, session.Suspicious_Flags)
                .Set(s => s.SuspiciousDetectedAt, session.SuspiciousDetectedAt);

            await _collection.UpdateOneAsync(filter, update);
        }





    }
}
