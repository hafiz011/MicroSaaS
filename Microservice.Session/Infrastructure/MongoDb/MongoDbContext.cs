using Microservice.Session.Entities;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Microservice.Session.Infrastructure.MongoDb
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
        public IMongoCollection<Users> UserDB => _database.GetCollection<Users>("User_Info");
        public IMongoCollection<Sessions> SessionsDB => _database.GetCollection<Sessions>("Sessions");
        public IMongoCollection<Tenants> ApiKey => _database.GetCollection<Tenants>("Clients_API");


    }
}
