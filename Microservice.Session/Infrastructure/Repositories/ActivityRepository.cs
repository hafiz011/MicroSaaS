using Microservice.Session.Models.DTOs;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.MongoDb;
using MongoDB.Driver;
using Microservice.Session.Infrastructure.Interfaces;

namespace Microservice.Session.Infrastructure.Repositories
{
    public class ActivityRepository : IActivityRepository
    {
        private readonly IMongoCollection<ActivityLog> _collection;


        public ActivityRepository(MongoDbContext context)
        {
            _collection = context.ActivityLogDB;
        }

        public async Task<IEnumerable<ActivityLog>> GetUserActivitiesAsync(string id)
        {
            return await _collection.Find(x => x.Id == id).ToListAsync();
        }

        public async Task CreateLogActivityAsync(ActivityLog log)
        {
            await _collection.InsertOneAsync(log);
        }
    }
}
