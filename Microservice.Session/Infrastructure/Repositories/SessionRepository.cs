using Google.Protobuf.WellKnownTypes;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Infrastructure.MongoDb;
using Microsoft.Extensions.Primitives;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.RegularExpressions;

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

        // get session by id and tenant id
        public async Task<Sessions> GetSessionByIdAsync(string existingSessionId, string tenantId)
        {
            var filterBuilder = Builders<Sessions>.Filter;
            var filters = new List<FilterDefinition<Sessions>>
            {
                filterBuilder.Eq(x => x.Tenant_Id, tenantId),
                filterBuilder.Eq(x => x.Id, existingSessionId)
            };

            var filter = filterBuilder.And(filters);
            var sort = Builders<Sessions>.Sort.Descending(x => x.Login_Time);

            return await _collection.Find(filter).Sort(sort).FirstOrDefaultAsync();
        }

        public async Task<Sessions> UpdateSessionAsync(Sessions session)
        {
            var filter = Builders<Sessions>.Filter.Where(a => a.Tenant_Id == session.Tenant_Id && a.Id == session.Id);

            var update = Builders<Sessions>.Update
                .Set(s => s.User_Id, session.User_Id)
                .Set(s => s.isActive, session.isActive)
                .Set(s => s.Logout_Time, session.Logout_Time);

            return await _collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Sessions>
                {
                    ReturnDocument = ReturnDocument.After // updated document fached
                });
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

        // suspicious session update
        public async Task<bool> UpdateSuspicious(string tenantId, string sessionId, double riskScore, bool isSuspicious)
        {
            var filterBuilder = Builders<Sessions>.Filter;
            var filters = new List<FilterDefinition<Sessions>>
            {
                filterBuilder.Eq(x => x.Tenant_Id, tenantId),
                filterBuilder.Eq(x => x.Id, sessionId)
            };

            var filter = filterBuilder.And(filters);

            var updateDef = Builders<Sessions>.Update
                .Set(s => s.Session_RiskScore, riskScore)
                .Set(s => s.isSuspicious, isSuspicious);

            var result = await _collection.UpdateOneAsync(filter, updateDef);

            return result.ModifiedCount > 0;
        }


        // helper for active session list & analytics queries
        private FilterDefinition<Sessions> BuildSessionFilter(string tenantId, DateTime? from, DateTime? to, string device, string country)
        {
            var filterBuilder = Builders<Sessions>.Filter;
            var filters = new List<FilterDefinition<Sessions>>
            {
                filterBuilder.Eq(s => s.Tenant_Id, tenantId)
            };

            if (from.HasValue) filters.Add(filterBuilder.Gte(s => s.Login_Time, from.Value));
            if (to.HasValue) filters.Add(filterBuilder.Lte(s => s.Login_Time, to.Value));

            if (!string.IsNullOrEmpty(device))
                filters.Add(filterBuilder.Regex(s => s.Device.Device_Type,
                    new BsonRegularExpression($"^{Regex.Escape(device)}$", "i")));

            if (!string.IsNullOrEmpty(country))
                filters.Add(filterBuilder.Regex(s => s.Geo_Location.Country,
                    new BsonRegularExpression($"^{Regex.Escape(country)}$", "i")));

            return filterBuilder.And(filters);
        }

        // get active sessions
        public async Task<List<Sessions>> ActiveSessionList(string tenantId, DateTime? from, DateTime? to, string device, string country)
        {
            var filter = BuildSessionFilter(tenantId, from, to, device, country);
            return await _collection.Find(filter)
                                    .SortByDescending(s => s.Login_Time)
                                    .ToListAsync();
        }

        // sessions analytics chart
        public async Task<List<Sessions>> GetSessionsAnalytics(string tenantId, DateTime? from, DateTime? to, string device, string country)
        {
            var filter = BuildSessionFilter(tenantId, from, to, device, country);
            return await _collection.Find(filter)
                                    .SortByDescending(s => s.Login_Time)
                                    .ToListAsync();
        }

    }
}
