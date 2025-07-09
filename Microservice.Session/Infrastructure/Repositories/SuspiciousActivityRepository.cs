using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Models.DashboardDTOs;
using MongoDB.Driver;

namespace Microservice.Session.Infrastructure.Repositories
{
    public class SuspiciousActivityRepository : ISuspiciousActivityRepository
    {
        private readonly IMongoCollection<SuspiciousActivity> _suspiciousCollection;
        private readonly IMongoCollection<Sessions> _sessionCollection;

        public SuspiciousActivityRepository(IMongoDatabase db)
        {
            _suspiciousCollection = db.GetCollection<SuspiciousActivity>("SuspiciousActivity");
            _sessionCollection = db.GetCollection<Sessions>("Sessions");
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

        public async Task<List<SuspiciousWithSessionDto>> GetSuspiciousWithSessionDetailsAsync(string tenantId, DateTime? from = null, DateTime? to = null)
        {
            var suspiciousList = await GetByTenantAsync(tenantId, from, to);
            var sessionIds = suspiciousList.Select(x => x.SessionId).ToList();

            var sessions = await _sessionCollection.Find(x => sessionIds.Contains(x.Id)).ToListAsync();

            return suspiciousList.Select(s => new SuspiciousWithSessionDto
            {
                Suspicious = s,
                SessionDetails = sessions.FirstOrDefault(sess => sess.Id == s.SessionId)
            }).ToList();
        }

    }
}
