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
                // session list filter
                new(
                    Builders<Sessions>.IndexKeys
                        .Ascending(x => x.Tenant_Id)
                        .Descending(x => x.Login_Time)
                        .Ascending(x => x.Device.Device_Type)
                        .Ascending(x => x.Geo_Location.Country),
                    new CreateIndexOptions { Name = "idx_session_filter" }
                ),

                // Suspicious session check (per user recent sessions)
                new(
                    Builders<Sessions>.IndexKeys
                        .Ascending(x => x.Tenant_Id)
                        .Ascending(x => x.User_Id)
                        .Ascending(x => x.Id)
                        .Descending(x => x.Login_Time),
                    new CreateIndexOptions { Name = "idx_suspicious_check" }
                ),

                // Get session by Id + Tenant
                new(
                    Builders<Sessions>.IndexKeys
                        .Ascending(x => x.Tenant_Id)
                        .Ascending(x => x.Id),
                    new CreateIndexOptions { Name = "idx_session_by_tenant_and_id", Unique = true }
                )
            };

            await collection.Indexes.CreateManyAsync(indexes);
            Console.WriteLine("MongoDB Session indexes created.");
        }

        private async Task SeedUserIndexesAsync()
        {
            var collection = _context.UserDB;

            var indexes = new List<CreateIndexModel<Users>>
            {
                // get user info
                new(
                    Builders<Users>.IndexKeys
                        .Ascending(x => x.Tenant_Id)
                        .Ascending(x => x.User_Id),
                    new CreateIndexOptions { Name = "idx_user_info_filter" }
                )
            };
            await collection.Indexes.CreateManyAsync(indexes);
            Console.WriteLine("MongoDB User indexes created.");
        }

        private async Task SeedApiKeyIndexesAsync()
        {
            var collection = _context.ApiKey;

            var indexes = new List<CreateIndexModel<Tenants>>
            {
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.UserId)),
                new CreateIndexModel<Tenants>(Builders<Tenants>.IndexKeys.Ascending(x => x.ApiSecret)),

                // Validate api key
                new(
                    Builders<Tenants>.IndexKeys
                        .Ascending(x => x.ApiSecret)
                        .Ascending(x => x.ExpirationDate)
                        .Ascending(x => x.IsRevoked),
                    new CreateIndexOptions { Name = "idx_validate_apikey" }
                )
            };

            await collection.Indexes.CreateManyAsync(indexes);
            Console.WriteLine("MongoDB API Key indexes created.");
        }
    }
}
