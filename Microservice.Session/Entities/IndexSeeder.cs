using MongoDB.Driver;

namespace Microservice.Session.Entities
{
    public class IndexSeeder
    {
        private readonly IMongoDatabase _db;

        public IndexSeeder(IMongoDatabase database)
        {
            _db = database;
        }

        public async Task SeedIndexesAsync()
        {
            var sessionCollection = _db.GetCollection<Sessions>("Sessions");
            var suspiciousCollection = _db.GetCollection<SuspiciousActivity>("SuspiciousSessions");

            var suspiciousIndexes = new[]
            {
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.SessionId)),
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.TenantId)),
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Descending(x => x.DetectedAt)),
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.IsSuspicious))
            };

            var sessionIndexes = new[]
            {
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Ascending(x => x.Id)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Ascending(x => x.User_Id)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Ascending(x => x.Tenant_Id)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Ascending(x => x.Device.Fingerprint)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Descending(x => x.Local_Time))
            };


            await sessionCollection.Indexes.CreateManyAsync(sessionIndexes);
            await suspiciousCollection.Indexes.CreateManyAsync(suspiciousIndexes);
        }
    }
}
