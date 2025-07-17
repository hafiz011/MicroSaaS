using Microservice.AuthService.Database;
using Microservice.AuthService.Models;
using MongoDB.Driver;

namespace Microservice.AuthService.Entities
{
    public class IndexSeeder
    {
        private readonly MongoDbContext _context;

        public IndexSeeder(MongoDbContext context)
        {
            _context = context;
        }

        public async Task SeedIndexesAsync()
        {
            var suspiciousCollection = _context.SuspiciousSessions;
            var userCollection = _context.Users;

            var suspiciousIndexes = new[]
            {
            new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.SessionId)),
            new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.TenantId)),
            new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Descending(x => x.DetectedAt)),
            new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.IsSuspicious)),
            new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.Device.Device_Type)),
            new CreateIndexModel<SuspiciousActivity>(Builders<SuspiciousActivity>.IndexKeys.Ascending(x => x.Geo_Location.Country))
            };

            var userIndexes = new[]
            {
            new CreateIndexModel<ApplicationUser>(Builders<ApplicationUser>.IndexKeys.Ascending(x => x.TenantId))
            };

            await suspiciousCollection.Indexes.CreateManyAsync(suspiciousIndexes);
            await userCollection.Indexes.CreateManyAsync(userIndexes);

            Console.WriteLine("MongoDB indexes created.");
        }
    }

}
