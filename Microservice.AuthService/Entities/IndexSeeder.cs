using MongoDB.Driver;

namespace Microservice.AuthService.Entities
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
            var suspiciousCollection = _db.GetCollection<SuspiciousActivity>("SuspiciousSessions");

            var suspiciousIndexes = new[]
            {
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.SessionId)),
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.TenantId)),
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Descending(x => x.DetectedAt)),
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.IsSuspicious))
            };

            await suspiciousCollection.Indexes.CreateManyAsync(suspiciousIndexes);
        }
    }
}
