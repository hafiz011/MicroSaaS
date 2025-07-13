using Microservice.AuthService.Entities;
using Microservice.AuthService.Models;
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

        public IMongoCollection<SuspiciousActivity> SuspiciousSessions => _database.GetCollection<SuspiciousActivity>("SuspiciousSessions");

        public IMongoCollection<ApplicationUser> Users => _database.GetCollection<ApplicationUser>("applicationUsers");

    }
}
