using Microservice.AuthService.Entities;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Microservice.AuthService.Database
{
    public class MongoDbContext
    {
        private readonly IMongoDatabase _database;

        public MongoDbContext(IOptions<MongoDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            _database = client.GetDatabase(settings.Value.DatabaseName);
        }

        public IMongoCollection<ActivityLog> ActivityLogDB => _database.GetCollection<ActivityLog>("Activity_Log");



    }
}
