using Microservice.Session.Infrastructure.MongoDb;
using Microservice.Session.Protos;
using MongoDB.Driver;

namespace Microservice.Session.Entities
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
            var sessionCollection = _context.SessionsDB;
            var UserCollection = _context.UserDB;
            var ApiKeyCollection = _context.ApiKey;

            var sessionIndexes = new[]
            {
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Ascending(x => x.Tenant_Id)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Ascending(x => x.User_Id)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Descending(x => x.Device.Fingerprint)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Ascending(x => x.Device.Device_Type)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Ascending(x => x.Geo_Location.Country)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Descending(x => x.Local_Time)),
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Ascending(x => x.isActive))
            };

            var userIndexs = new[] 
            {
                new CreateIndexModel<Users>(Builders<Users>.IndexKeys.Ascending(x => x.Tenant_Id)),
                new CreateIndexModel<Users>(Builders<Users>.IndexKeys.Ascending(x => x.User_Id))
            };

            var apikeyIndexes = new[]
            {
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.UserId)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.ApiSecret)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.Plan)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.ExpirationDate)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.RequestLimit)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.Created_At)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.IsRevoked)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.IsRevoked))
            };



            await sessionCollection.Indexes.CreateManyAsync(sessionIndexes);
            Console.WriteLine("MongoDB session indexes created.");
            await UserCollection.Indexes.CreateManyAsync(userIndexs);
            Console.WriteLine("MongoDB user index created");
            await ApiKeyCollection.Indexes.CreateManyAsync(apikeyIndexes);
            Console.WriteLine("MongoDB client api index created");

        }
    }
}
