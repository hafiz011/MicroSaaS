using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Protos;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using ProtoSession = Microservice.Session.Protos.Session;

namespace Microservice.Session.Infrastructure.Services
{
    public class GrpcServer : ApiKey.ApiKeyBase
    {
        private readonly IApiKeyRepository _apiKeyRepository;
        private readonly IUserInfoRepository _userinfoRepository;
        private readonly ISessionRepository _sessionRepository;

        public GrpcServer(IApiKeyRepository apiKeyRepository, IUserInfoRepository userInfoRepository, ISessionRepository sessionRepository)
        {
            _apiKeyRepository = apiKeyRepository;
            _userinfoRepository = userInfoRepository;
            _sessionRepository = sessionRepository;

        }

        // api key info. this is used to get api key for a user
        public override async Task<ApiKeyResponse> GetApiKey(ApiKeyRequest request, ServerCallContext context)
        {
            var apiKey = await _apiKeyRepository.GetApiByUserIdAsync(request.UserId);

            if (apiKey == null)
                throw new RpcException(new Status(StatusCode.NotFound, $"API key not found for user {request.UserId}"));

            return new ApiKeyResponse
            {
                UserId = apiKey.UserId,
                OrgName = apiKey.Org_Name,
                Domain = apiKey.Domain,
                OrgEmail = apiKey.Org_Email,
                Plan = apiKey.Plan,
                ExpirationDate = Timestamp.FromDateTime(apiKey.ExpirationDate.ToUniversalTime()),
                CreatedAt = Timestamp.FromDateTime(apiKey.Created_At.ToUniversalTime()),
                RequestLimit = apiKey.RequestLimit,
                ResetDate = apiKey.ResetDate.HasValue
                    ? Timestamp.FromDateTime(apiKey.ResetDate.Value.ToUniversalTime())
                    : null, // Handle nullable DateTime
                IsRevoked = apiKey.IsRevoked,
                IsActive = apiKey.IsActive,
                TanantId = apiKey.Id
            };
        }

        // create api key. This is used to create an API key for a user
        public override async Task<ApiHashResponse> CreateApiKey(CreateApiKeyRequest request, ServerCallContext context)
        {
            var existingKey = await _apiKeyRepository.GetApiByUserIdAsync(request.UserId);
            if (existingKey != null)
                throw new RpcException(new Status(StatusCode.AlreadyExists, $"API key already exists for user {request.UserId}"));

            var (rawKey, hashedKey) = ApiKeyGenerator.GenerateApiKey();

            var key = new Tenants
            {
                UserId = request.UserId,
                ApiSecret = hashedKey,
                Org_Name = request.OrgName,
                Domain = request.Domain,
                Org_Email = request.OrgEmail,
                Plan = request.Plan,
                Created_At = request.CreatedAt.ToDateTime(),
                ExpirationDate = request.ExpirationDate.ToDateTime(),
                RequestLimit = request.RequestLimit,
                IsRevoked = false,
            };

            var tenant = await _apiKeyRepository.CreateApiKeyAsync(key);

            return new ApiHashResponse
            {
                ApiHash = rawKey,
                TanantId = tenant.Id
            };
        }

        // regenerate api key. This is used to regenerate an API key for a user
        public override async Task<ApiHashResponse> RegenerateApiKey(ApiKeyRequest request, ServerCallContext context)
        {
            var existingKey = await _apiKeyRepository.GetApiByUserIdAsync(request.UserId);
            if (existingKey == null)
                throw new RpcException(new Status(StatusCode.NotFound, $"API key not found for user {request.UserId}"));

            var (rawKey, hashedKey) = ApiKeyGenerator.GenerateApiKey();

            var newKey = new Tenants
            {
                UserId = request.UserId,
                ApiSecret = hashedKey
            };

            var success = await _apiKeyRepository.RegenerateApiKeyAsync(newKey);
            return new ApiHashResponse
            {
                ApiHash = rawKey
            };
        }

        // renew api key. this is used to renew an API key for a user
        public override async Task<ApiKeyResponse> RenewApiKey(RenewApiKeyRequest request, ServerCallContext context)
        {
            var existingKey = await _apiKeyRepository.GetApiByUserIdAsync(request.UserId);
            if (existingKey == null)
                throw new RpcException(new Status(StatusCode.NotFound, $"API key not found for user {request.UserId}"));

            // Update fields
            existingKey.Plan = request.Plan;
            existingKey.RequestLimit = request.RequestLimit;
            existingKey.ExpirationDate = request.ExpirationDate.ToDateTime();
            existingKey.IsRevoked = request.IsRevoked;
            existingKey.ResetDate = DateTime.UtcNow;

            var success = await _apiKeyRepository.RenewApiKeyAsync(existingKey);
            if (!success)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Failed to renew API key for user {request.UserId}"));

            // Fetch updated key
            var apiKey = await _apiKeyRepository.GetApiByUserIdAsync(request.UserId);

            return new ApiKeyResponse
            {
                UserId = apiKey.UserId,
                OrgName = apiKey.Org_Name,
                Domain = apiKey.Domain,
                OrgEmail = apiKey.Org_Email,
                Plan = apiKey.Plan,
                ExpirationDate = Timestamp.FromDateTime(apiKey.ExpirationDate.ToUniversalTime()),
                CreatedAt = Timestamp.FromDateTime(apiKey.Created_At.ToUniversalTime()),
                RequestLimit = apiKey.RequestLimit,
                ResetDate = apiKey.ResetDate.HasValue
                    ? Timestamp.FromDateTime(apiKey.ResetDate.Value.ToUniversalTime())
                    : null,
                IsRevoked = apiKey.IsRevoked,
                IsActive = apiKey.IsActive
            };
        }

        // revike api key. this is used to revoke an API key for a user
        public override async Task<ApiKeyResponse> RevokeApiKey(ApiKeyRequest request, ServerCallContext context)
        {
            var success = await _apiKeyRepository.RevokeApiKeyAsync(request.UserId);
            if (!success)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Failed to Revoke API key for user {request.UserId}"));
           
            var apiKey = await _apiKeyRepository.GetApiByUserIdAsync(request.UserId);
            return new ApiKeyResponse
            {
                UserId = apiKey.UserId,
                OrgName = apiKey.Org_Name,
                Domain = apiKey.Domain,
                OrgEmail = apiKey.Org_Email,
                Plan = apiKey.Plan,
                ExpirationDate = Timestamp.FromDateTime(apiKey.ExpirationDate.ToUniversalTime()),
                CreatedAt = Timestamp.FromDateTime(apiKey.Created_At.ToUniversalTime()),
                RequestLimit = apiKey.RequestLimit,
                ResetDate = apiKey.ResetDate.HasValue
                   ? Timestamp.FromDateTime(apiKey.ResetDate.Value.ToUniversalTime())
                   : null,
                IsRevoked = apiKey.IsRevoked,
                IsActive = apiKey.IsActive
            };
        }

        // curenctly this is not used. This is used to get api keys for a user
        // user info. This is used to get user information by userId and tenantId
        public override async Task<UserInfoResponse> GetUserInfo(UserInfoRequest request, ServerCallContext context)
        {
            var getinfo = await _userinfoRepository.getUserById(request.UserId, request.TenantId);
            if (getinfo != null)
                throw new RpcException(new Status(StatusCode.NotFound, $"User not found {request.UserId}"));

            return new UserInfoResponse
            {
                UserName = getinfo.Name,
                UserEmail = getinfo.Email,
                Lastlogin = getinfo.Last_login.ToString()
            };
        }

        // active session and user list. This is used to get active users sessions for a tenant
        public override async Task<SessionListResponse> GetSessionList(SessionListRequest request, ServerCallContext context)
        {
            var sessions = await _sessionRepository.ActiveSessionList(
                request.TenantId,
                request.From?.ToDateTime(),
                request.To?.ToDateTime(),
                request.Device,
                request.Country
            );

            if (sessions == null || !sessions.Any())
                throw new RpcException(new Status(StatusCode.NotFound, $"No active sessions found for TenantId {request.TenantId}"));

            var userIds = sessions.Select(s => s.User_Id).Distinct().ToList();
            var users = await _userinfoRepository.GetUserBySessionIdListAsync(request.TenantId, userIds);

            var response = new SessionListResponse();

            foreach (var session in sessions)
            {
                var user = users.FirstOrDefault(u => u.User_Id == session.User_Id);

                response.Sessions.Add(new ProtoSession
                {
                    UserName = user?.Name ?? "",
                    Email = user?.Email ?? "",
                    UserId = session.User_Id ?? "",
                    IpAddress = session.Ip_Address ?? "",
                    City = session.Geo_Location?.City ?? "",
                    Country = session.Geo_Location?.Country ?? "",
                    Status = session.isActive ? "Active" : "Inactive",
                    DeviceType = session.Device?.Device_Type ?? "",
                    LoginTime = Timestamp.FromDateTime(session.Login_Time.ToUniversalTime()),
                    Lac = session.Geo_Location?.Latitude_Longitude ?? "",
                    Sessionid = session.Id.ToString()
                });
            }
            return response;
        }

        // session check for suspicious detection
        public override async Task<SessionCheckResponce> SessionListCheck(SessionCheckRequest request, ServerCallContext context)
        {
            var sessions = await _sessionRepository.GetSessionCheckListAsync(
                request.TenantId,
                request.UserId,
                request.SessionId,
                request.V
            );

            if (sessions == null || sessions.Count == 0)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"No sessions found for TenantId {request.TenantId} and UserId {request.UserId}"));
            }

            var response = new SessionCheckResponce();

            foreach (var session in sessions)
            {
                var check = new SessionCheck
                {
                    IpAddress = session.Ip_Address ?? "",
                    Country = session.Geo_Location?.Country ?? "",
                    Fingerprint = session.Device?.Fingerprint ?? "",
                    IsVPN = session?.isVPN ?? false,
                    LatLon = session.Geo_Location?.Latitude_Longitude ?? "",
                    LocalTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(session.Local_Time.ToUniversalTime()),
                    LoginTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(session.Login_Time.ToUniversalTime()),
                    LogoutTime = session.Logout_Time.HasValue
                        ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(session.Logout_Time.Value.ToUniversalTime())
                        : null
                };

                response.Sessionlist.Add(check);
            }

            return response;
        }

        // Session Analytics. This is used to get session analytics for a tenant
        public override async Task<SessionAnalyticsResponse> GetSessionAnalytics(SessionListRequest request, ServerCallContext context)
        {
            var sessions = await _sessionRepository.GetSessionsAnalytics(
                request.TenantId,
                request.From?.ToDateTime(),
                request.To?.ToDateTime(),
                request.Device,
                request.Country
            );

            var response = new SessionAnalyticsResponse();
            if (sessions == null || !sessions.Any())
                return response; // return empty response if no data

            // ---------------- Daily Sessions ----------------
            var dailyGroups = sessions
                .GroupBy(s => s.Login_Time.Date)
                .Select(g => new DailySession
                {
                    Date = g.Key.ToString("yyyy-MM-dd"), // ISO format
                    Sessions = g.Count(),
                    Suspicious = g.Count(s => s.isSuspicious)
                })
                .OrderByDescending(x => x.Date) // latest date first
                .ToList();

            response.DailySessions.AddRange(dailyGroups);


            // ---------------- Device distribution ----------------
            var deviceDist = sessions
                .Where(s => s.Device != null && !string.IsNullOrEmpty(s.Device.Device_Type))
                .GroupBy(s => s.Device.Device_Type.ToLower())
                .Select(g => new DeviceDistribution
                {
                    Name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(g.Key), // Mobile/Desktop/Tablet
                    Total = g.Count(), // total sessions per device type
                    AvgDuration = g
                        .Where(s => s.Login_Time != null && s.Logout_Time.HasValue)
                        .Select(s => (s.Logout_Time.Value - s.Login_Time).TotalSeconds)
                        .DefaultIfEmpty(0)
                        .Average(), // average session duration in seconds
                    AvgActions = g
                        .Select(s => s.ActionCount)
                        .DefaultIfEmpty(0)
                        .Average() // average actions per session
                })
                .OrderByDescending(d => d.Total) // sort by most used device
                .ToList();

            response.DeviceDistribution.AddRange(deviceDist);




             // ---------------- Country Distribution ----------------
             //var countryDist = sessions
             //    .Where(s => s.Geo_Location != null && !string.IsNullOrEmpty(s.Geo_Location.Country))
             //    .GroupBy(s => s.Geo_Location.Country)
             //    .Select(g => new CountryDistribution
             //    {
             //        Name = g.Key,
             //        Value = g.Count()
             //    })
             //    .ToList();

             //response.CountryDistribution.AddRange(countryDist);

             //var activeUsers = sessions
             //    .Select(s => s.User_Id && s.isActive)
             //    .Distinct()
             //    .Count();


             // ---------------- Session Metrics ----------------
             var totalSessions = sessions.Count;

            // Bounce rate (<30 sec session)
            var bounceCount = sessions.Count(s => s.Logout_Time.HasValue &&
                                                  (s.Logout_Time.Value - s.Login_Time).TotalSeconds < 30);

            var bounceRate = totalSessions > 0 ? (double)bounceCount / totalSessions * 100 : 0;

            // Avg session duration (seconds)
            var avgDuration = sessions
                .Where(s => s.Logout_Time.HasValue)
                .Select(s => (s.Logout_Time.Value - s.Login_Time).TotalSeconds)
                .DefaultIfEmpty(0)
                .Average();

            // ---------------- Avg Actions ----------------
            var avgActions = sessions
                .Select(s => s.ActionCount)   // ensure ActionCount exists in your session model
                .DefaultIfEmpty(0)
                .Average();

            // ---------------- Trends Calculation ----------------
            var from = request.From?.ToDateTime();
            var to = request.To?.ToDateTime();

            if (from.HasValue && to.HasValue)
            {
                var duration = to.Value - from.Value;

                var prevTo = from.Value.AddTicks(-1);
                var prevFrom = prevTo - duration;

                var previousSessions = await _sessionRepository.GetSessionsAnalytics(
                    request.TenantId,
                    prevFrom,
                    prevTo,
                    request.Device,
                    request.Country
                );

                var totalPreviousSessions = previousSessions.Count;

                // previous Avg session duration (seconds)
                var previousAvgDuration = previousSessions
                    .Where(s => s.Logout_Time.HasValue)
                    .Select(s => (s.Logout_Time.Value - s.Login_Time).TotalSeconds)
                    .DefaultIfEmpty(0)
                    .Average();

                var avgDurationTrend = previousAvgDuration > 0
                    ? ((avgDuration - previousAvgDuration) / previousAvgDuration) * 100
                    : 0;

                // previous Bounce rate (<30 sec session)
                var previousBounceCount = previousSessions.Count(s => s.Logout_Time.HasValue &&
                                                          (s.Logout_Time.Value - s.Login_Time).TotalSeconds < 30);

                var previousBounceRate = totalPreviousSessions > 0
                    ? (double)previousBounceCount / totalPreviousSessions * 100
                    : 0;

                var bounceRateTrend = previousBounceRate > 0
                    ? ((bounceRate - previousBounceRate) / previousBounceRate) * 100
                    : 0;

                // Previous sessions to avgActions
                var previousAvgActions = previousSessions
                    .Select(s => s.ActionCount)
                    .DefaultIfEmpty(0)
                    .Average();

                // Trend for avgActions
                var avgActionsTrend = previousAvgActions > 0
                    ? ((avgActions - previousAvgActions) / previousAvgActions) * 100
                    : 0;

                response.SessionMetrics = new SessionMetrics
                {
                    AvgDuration = avgDuration,
                    AvgDurationTrend = avgDurationTrend,
                    BounceRate = bounceRate,
                    BounceRateTrend = bounceRateTrend,
                    AvgActions = avgActions,
                    AvgActionsTrend = avgActionsTrend
                };
            }
            else
            {
                response.SessionMetrics = new SessionMetrics
                {
                    AvgDuration = avgDuration,
                    AvgDurationTrend = 0,
                    BounceRate = bounceRate,
                    BounceRateTrend = 0,
                    AvgActions = avgActions,
                    AvgActionsTrend = 0,
                };
            }

            return response;
        }


    }
}
