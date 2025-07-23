using Microservice.Session.Infrastructure.MongoDb;
using Microservice.Session.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Hosting;

namespace Microservice.Session.Infrastructure.Services
{
    public class SessionTimeOutService : BackgroundService
    {
        private readonly ILogger<SessionTimeOutService> _logger;
        private readonly IMongoCollection<Sessions> _sessionsCollection;
        private readonly IMongoCollection<ActivityLog> _activityLogsCollection;

        public SessionTimeOutService(ILogger<SessionTimeOutService> logger, MongoDbContext context)
        {
            _logger = logger;
            _sessionsCollection = context.SessionsDB;
            _activityLogsCollection = context.ActivityLogDB;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Running session cleanup...");

                    const int batchSize = 100;
                    const int idleMinutes = 20;
                    ObjectId? lastId = null;

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var filter = Builders<Sessions>.Filter.Eq(s => s.isActive, true);
                        if (lastId.HasValue)
                        {
                            filter &= Builders<Sessions>.Filter.Gt("_id", lastId.Value);
                        }

                        var sessionsBatch = await _sessionsCollection
                            .Find(filter)
                            .SortBy(s => s.Id)
                            .Limit(batchSize)
                            .ToListAsync(stoppingToken);

                        if (!sessionsBatch.Any()) break;

                        int markedInactiveCount = 0;

                        foreach (var session in sessionsBatch)
                        {
                            var lastActivity = await _activityLogsCollection
                                .Find(a => a.Session_Id == session.Id)
                                .SortByDescending(a => a.Time_Stamp)
                                .Limit(1)
                                .FirstOrDefaultAsync(stoppingToken);

                            if (lastActivity == null) continue;

                            var idleTime = DateTime.UtcNow - lastActivity.Time_Stamp;
                            if (idleTime.TotalMinutes < idleMinutes) continue;

                            var update = Builders<Sessions>.Update
                                .Set(s => s.Logout_Time, lastActivity.Time_Stamp.AddMinutes(3))
                                .Set(s => s.isActive, false);

                            await _sessionsCollection.UpdateOneAsync(
                                Builders<Sessions>.Filter.Eq(s => s.Id, session.Id),
                                update,
                                cancellationToken: stoppingToken
                            );

                            markedInactiveCount++;
                        }

                        _logger.LogInformation($"Batch processed. {markedInactiveCount} sessions marked inactive.");
                        lastId = new ObjectId(sessionsBatch.Last().Id);
                    }

                    _logger.LogInformation("Session cleanup finished.");
                    await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in SessionTimeOutService.");
                }
            }
        }
    }
}
