using Microservice.AuthService.Entities;
using Microservice.AuthService.Infrastructure.Interfaces;
using Microservice.Session.Entities;
using MongoDB.Driver;

namespace Microservice.AuthService.Infrastructure.Repositories
{
    public class SuspiciousActivityRepository : ISuspiciousActivityRepository
    {
        private readonly IMongoCollection<SuspiciousActivity> _suspiciousCollection;


        public SuspiciousActivityRepository(IMongoDatabase db)
        {
            _suspiciousCollection = db.GetCollection<SuspiciousActivity>("SuspiciousActivity");

        }

        public async Task InsertAsync(SuspiciousActivity activity)
        {
            await _suspiciousCollection.InsertOneAsync(activity);
        }

        public async Task<List<SuspiciousActivity>> GetByTenantAsync(string tenantId, DateTime? from = null, DateTime? to = null)
        {
            var filterBuilder = Builders<SuspiciousActivity>.Filter;
            var filters = new List<FilterDefinition<SuspiciousActivity>>
            {
                filterBuilder.Eq(x => x.TenantId, tenantId),
                filterBuilder.Eq(x => x.IsSuspicious, true)
            };

            if (from.HasValue)
                filters.Add(filterBuilder.Gte(x => x.DetectedAt, from.Value));
            if (to.HasValue)
                filters.Add(filterBuilder.Lte(x => x.DetectedAt, to.Value));

            return await _suspiciousCollection.Find(filterBuilder.And(filters)).ToListAsync();
        }

        public async Task UpdateSuspiciousStatusAsync(string tenantId, string sessionId)
        {
            var filterBuilder = Builders<SuspiciousActivity>.Filter;
            var updateBuilder = Builders<SuspiciousActivity>.Update;

            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.TenantId, tenantId),
                filterBuilder.Eq(x => x.SessionId, sessionId),
                filterBuilder.Eq(x => x.IsSuspicious, true)
            );

            var update = updateBuilder
                .Set(x => x.IsSuspicious, false)
                .Set(x => x.RiskLevel, "cleared")
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            await _suspiciousCollection.UpdateManyAsync(filter, update);
        }

        public async Task<SuspiciousActivity> GetBySessionIdAsync(string tenantId, string sessionId)
        {
            var filterBuilder = Builders<SuspiciousActivity>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.TenantId, tenantId),
                filterBuilder.Eq(x => x.SessionId, sessionId),
                filterBuilder.Eq(x => x.IsSuspicious, true)
            );

            return await _suspiciousCollection.Find(filter).FirstOrDefaultAsync();
        }


        //public async Task<List<SuspiciousWithSessionDto>> GetSuspiciousWithSessionDetailsAsync(string tenantId, DateTime? from = null, DateTime? to = null)
        //{
        //    var suspiciousList = await GetByTenantAsync(tenantId, from, to);
        //    var sessionIds = suspiciousList.Select(x => x.SessionId).ToList();

        //    var sessions = await _sessionCollection.Find(x => sessionIds.Contains(x.Id)).ToListAsync();

        //    return suspiciousList.Select(s => new SuspiciousWithSessionDto
        //    {
        //        Suspicious = s,
        //        SessionDetails = sessions.FirstOrDefault(sess => sess.Id == s.SessionId)
        //    }).ToList();
        //}

    }
}
