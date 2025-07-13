using Microservice.AuthService.Models;
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
            var applicationUserCollection = _db.GetCollection<ApplicationUser>("applicationUsers");

            var suspiciousIndexes = new[]
            {
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.SessionId)),
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.TenantId)),
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Descending(x => x.DetectedAt)),
                new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.IsSuspicious))
            };

            var applicationUserIndexes = new[]
            {
                new CreateIndexModel<ApplicationUser>(Builders<ApplicationUser>.IndexKeys.Ascending(x => x.TenantId))
            };




            await suspiciousCollection.Indexes.CreateManyAsync(suspiciousIndexes);
            await applicationUserCollection.Indexes.CreateManyAsync(applicationUserIndexes);
        }
    }
}
