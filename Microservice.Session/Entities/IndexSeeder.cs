using Microservice.Session.Infrastructure.MongoDb;
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
                new CreateIndexModel<Sessions>(Builders<Sessions>.IndexKeys.Descending(x => x.Local_Time))
            };


            await sessionCollection.Indexes.CreateManyAsync(sessionIndexes);
            Console.WriteLine("MongoDB indexes created.");
        }
    }
}
