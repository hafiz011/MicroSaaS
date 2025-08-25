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
            await SeedSessionIndexesAsync();
            await SeedUserIndexesAsync();
            await SeedApiKeyIndexesAsync();
        }

        private async Task SeedSessionIndexesAsync()
        {
            var collection = _context.SessionsDB;
            var indexes = new List<CreateIndexModel<Sessions>>
            {
                new(
                    Builders<Sessions>.IndexKeys
                        .Ascending(x => x.Tenant_Id)
                        .Ascending(x => x.isActive)
                        .Descending(x => x.Login_Time)
                        .Ascending(x => x.Device.Device_Type)
                        .Ascending(x => x.Geo_Location.Country),
                    new CreateIndexOptions { Name = "idx_active_session_filter" }
                ),

                new(
                    Builders<Sessions>.IndexKeys
                        .Ascending(x => x.Tenant_Id)
                        .Ascending(x => x.User_Id)
                        .Descending(x => x.Login_Time),
                    new CreateIndexOptions { Name = "idx_suspicious_check" }
                )
            };

            await collection.Indexes.CreateManyAsync(indexes);
            Console.WriteLine("MongoDB Session indexes created.");
        }

        private async Task SeedUserIndexesAsync()
        {
            var collection = _context.UserDB;

            var indexes = new[]
            {
                new CreateIndexModel<Users>(Builders<Users>.IndexKeys.Ascending(x => x.Tenant_Id)),
                new CreateIndexModel<Users>(Builders<Users>.IndexKeys.Ascending(x => x.User_Id))
            };

            await collection.Indexes.CreateManyAsync(indexes);
            Console.WriteLine("MongoDB User indexes created.");
        }

        private async Task SeedApiKeyIndexesAsync()
        {
            var collection = _context.ApiKey;

            var indexes = new[]
            {
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.UserId)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.ApiSecret)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.Plan)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.ExpirationDate)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.RequestLimit)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.Created_At)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.IsRevoked))
            };

            await collection.Indexes.CreateManyAsync(indexes);
            Console.WriteLine("MongoDB API Key indexes created.");
        }
    }
}
