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


        // update session
        public async Task UpdateSessionAsync(string sessionId, Sessions update)
        {
            var filter = Builders<Sessions>.Filter.Eq(s => s.Id, sessionId);
            var updates = new List<UpdateDefinition<Sessions>>();

            void AddUpdatesForObject(object obj, string prefix = "")
            {
                if (obj == null) return;

                var props = obj.GetType().GetProperties();
                foreach (var prop in props)
                {
                    var value = prop.GetValue(obj);

                    string fieldName = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    updates.Add(Builders<Sessions>.Update.Set(fieldName, value));
                }
            }

            // Top-level properties
            AddUpdatesForObject(update);

            // Nested objects
            AddUpdatesForObject(update.Geo_Location, "Geo_Location");
            AddUpdatesForObject(update.Device, "Device");

            if (updates.Any())
            {
                var combinedUpdate = Builders<Sessions>.Update.Combine(updates);
                await _collection.UpdateOneAsync(filter, combinedUpdate);
            }
        }



        //public async Task UpdateSessionAsync(string id, Sessions update)
        //{
        //    var filter = Builders<Sessions>.Filter.Eq(u => u.Id, id);

        //    var updateDef = Builders<Sessions>.Update
        //        .Set(s => s.Logout_Time, update.Logout_Time)
        //        .Set(s => s.isActive, update.isActive);

        //    await _collection.UpdateOneAsync(filter, updateDef);
        //}

        // active session list
        public async Task<List<Sessions>> ActiveSessionList(string tenantId, DateTime? from, DateTime? to, string device, string country)
        {
            var filterBuilder = Builders<Sessions>.Filter;
            var filters = new List<FilterDefinition<Sessions>>
            {
                filterBuilder.Eq(x => x.Tenant_Id, tenantId),
                //filterBuilder.Eq(x => x.isActive, true)
            };

            if (from.HasValue)
                filters.Add(filterBuilder.Gte(x => x.Login_Time, from.Value));
            if (to.HasValue)
                filters.Add(filterBuilder.Lte(x => x.Login_Time, to.Value));

            if (!string.IsNullOrEmpty(device))
            {
                filters.Add(filterBuilder.Regex(
                    x => x.Device.Device_Type,
                    new BsonRegularExpression($"^{Regex.Escape(device)}$", "i")  // case-insensitive
                ));
            }

            if (!string.IsNullOrEmpty(country))
            {
                filters.Add(filterBuilder.Regex(
                    x => x.Geo_Location.Country,
                    new BsonRegularExpression($"^{Regex.Escape(country)}$", "i")  // case-insensitive
                ));
            }

            var sort = Builders<Sessions>.Sort.Descending(x => x.Login_Time);

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

            if (from.HasValue)
                filters.Add(filterBuilder.Gte(x => x.Login_Time, from.Value));
            if (to.HasValue)
                filters.Add(filterBuilder.Lte(x => x.Login_Time, to.Value));

            if (!string.IsNullOrEmpty(device))
            {
                filters.Add(filterBuilder.Regex(
                    x => x.Device.Device_Type,
                    new BsonRegularExpression($"^{Regex.Escape(device)}$", "i")  // case-insensitive
                ));
            }

            if (!string.IsNullOrEmpty(country))
            {
                filters.Add(filterBuilder.Regex(
                    x => x.Geo_Location.Country,
                    new BsonRegularExpression($"^{Regex.Escape(country)}$", "i")  // case-insensitive
                ));
            }

            var sort = Builders<Sessions>.Sort.Descending(x => x.Login_Time);

            return await _collection.Find(filterBuilder.And(filters)).Sort(sort).ToListAsync();
        }
    }
}
