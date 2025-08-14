using Microservice.Session.Infrastructure.MongoDb;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microsoft.Extensions.Primitives;
using MongoDB.Driver;
using Google.Protobuf.WellKnownTypes;

namespace Microservice.Session.Infrastructure.Repositories
{
    public class SessionRepository : ISessionRepository
    {
        private readonly IMongoCollection<Sessions> _collection;
        public SessionRepository(MongoDbContext context) 
        {
            _collection = context.SessionsDB;
        }

        // create session
        public async Task<Sessions> CreateSessionAsync(Sessions sessions)
        {
            await _collection.InsertOneAsync(sessions);
            return sessions;
        }

        // end session
        public async Task EndSessionAsync(string sessionId)
        {
            var update = Builders<Sessions>.Update
                .Set(a => a.Logout_Time, DateTime.UtcNow)
                .Set(i => i.isActive, false);
            await _collection.UpdateOneAsync(s => s.Id == sessionId, update);
        }

        // get session by id
        public async Task<Sessions> GetSessionByIdAsync(string existingSessionId)
        {
            return await _collection.Find(a => a.Id == existingSessionId).FirstOrDefaultAsync();
        }

        // update session
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

        // active session list
        public async Task<List<Sessions>> ActiveSessionList(string tenantId, DateTime? from, DateTime? to, string device, string country)
        {
            var filterBuilder = Builders<Sessions>.Filter;
            var filters = new List<FilterDefinition<Sessions>>
            {
                filterBuilder.Eq(x => x.Tenant_Id, tenantId),
                filterBuilder.Eq(x => x.isActive, true)
            };

            if (!string.IsNullOrEmpty(device))
                filters.Add(filterBuilder.Eq(x => x.Device.Device_Type, device));

            if (!string.IsNullOrEmpty(country))
                filters.Add(filterBuilder.Eq(x => x.Geo_Location.Country, country));

            if (from.HasValue)
                filters.Add(filterBuilder.Gte(x => x.Local_Time, from.Value));
            if (to.HasValue)
                filters.Add(filterBuilder.Lte(x => x.Local_Time, to.Value));

            var sort = Builders<Sessions>.Sort.Descending(x => x.Local_Time);

            return await _collection.Find(filterBuilder.And(filters)).Sort(sort).ToListAsync();
        }


        // session check for suspicious detection
        public async Task<List<Sessions>> GetSessionCheckListAsync(string tenantId, string userId, string sessionId, int limit)
        {
            var filterBuilder = Builders<Sessions>.Filter;
            var filters = new List<FilterDefinition<Sessions>>
            {
                filterBuilder.Eq(x => x.Tenant_Id, tenantId),
                filterBuilder.Eq(x => x.User_Id, userId),
                filterBuilder.Ne(x => x.Id, sessionId) // exclude current session
            };

            var sort = Builders<Sessions>.Sort.Descending(x => x.Login_Time);

            return await _collection.Find(filterBuilder.And(filters)).Sort(sort).Limit(limit).ToListAsync();
        }

        // sessions analytics chart
        public async Task<List<Sessions>> GetSessionsAnalytics(string tenantId, DateTime? from, DateTime? to, string device, string country)
        {
            var filterBuilder = Builders<Sessions>.Filter;
            var filters = new List<FilterDefinition<Sessions>>
            {
                filterBuilder.Eq(x => x.Tenant_Id, tenantId),
            };

            if (!string.IsNullOrEmpty(device))
                filters.Add(filterBuilder.Eq(x => x.Device.Device_Type, device));

            if (!string.IsNullOrEmpty(country))
                filters.Add(filterBuilder.Eq(x => x.Geo_Location.Country, country));

            if (from.HasValue)
                filters.Add(filterBuilder.Gte(x => x.Local_Time, from.Value));
            if (to.HasValue)
                filters.Add(filterBuilder.Lte(x => x.Local_Time, to.Value));

            return await _collection.Find(filterBuilder.And(filters)).ToListAsync();
        }
    }
}
