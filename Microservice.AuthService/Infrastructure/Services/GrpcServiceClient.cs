using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microservice.AuthService.Protos;

namespace Microservice.AuthService.Infrastructure.Services
{
    public class GrpcServiceClient
    {
        private readonly ApiKey.ApiKeyClient _client;

        public GrpcServiceClient(IConfiguration config)
        {
            var grpcServerUrl = config["Grpc:ApiKeyServiceUrl"];
            var channel = GrpcChannel.ForAddress(grpcServerUrl);
            _client = new ApiKey.ApiKeyClient(channel);
        }

        // api key grpc serivice. This is used to get api key for a user
        public async Task<ApiKeyResponse> GetApiKeyAsync(string userId)
        {
            var request = new ApiKeyRequest { UserId = userId };
            return await _client.GetApiKeyAsync(request);
        }

        // create api key. This is used to create api key for a user
        public async Task<(string ApiHash, string TenantId)> CreateApiKeyAsync(CreateApiKeyRequest request)
        {
            var response = await _client.CreateApiKeyAsync(request);
            return (response.ApiHash, response.TanantId);
        }

        // regenerate api key. This is used to regenerate api key for a user
        public async Task<string> RegenerateApiKeyAsync(string userId)
        {
            var response = await _client.RegenerateApiKeyAsync(new ApiKeyRequest { UserId = userId });
            return response.ApiHash;
        }

        // renuw api key. this is used to renew api key for a user
        public async Task<ApiKeyResponse> RenewApiKeyAsync(RenewApiKeyRequest request)
        {
            return await _client.RenewApiKeyAsync(request);
        }

        // revocke api key. this is used to revoke api key for a user
        public async Task<ApiKeyResponse> RevokeApiKeyAsync(string userId)
        {
            return await _client.RevokeApiKeyAsync(new ApiKeyRequest { UserId = userId });
        }

        // user info grpc service
        public async Task<UserInfoResponse> GetUserInfo(string userId, string tenantId)
        {
            var response = await _client.GetUserInfoAsync(new UserInfoRequest { UserId = userId, TenantId = tenantId });
            return response;
        }

        // active user session list. this is used for dashboard controller to show active user sessions
        public async Task<SessionListResponse> GetSessionList(string tenantId, DateTime? from, DateTime? to, string device, string country)
        {
            var request = new SessionListRequest
            {
                TenantId = tenantId,
                Device = device ?? "",
                Country = country ?? ""
            };

            if (from.HasValue)
                request.From = Timestamp.FromDateTime(from.Value.ToUniversalTime());

            if (to.HasValue)
                request.To = Timestamp.FromDateTime(to.Value.ToUniversalTime());

            try
            {
                return await _client.GetSessionListAsync(request);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                // Return empty list instead of throwing
                return new SessionListResponse { Sessions = { }, TopUser = { } };
            }
        }


        // session check for suspicious detection. this is used rabbitmq consumer to check session list
        public async Task<List<SessionCheck>> SessionListCheck(string tenantId, string userId, string sessionId, int v)
        {
            var request = new SessionCheckRequest
            {
                TenantId = tenantId,
                UserId = userId,
                SessionId = sessionId,
                V = v
            };

            var response = await _client.SessionListCheckAsync(request);
            return response.Sessionlist.ToList();
        }

        // get session details
        //public async Task<SessiionDetails> GetSessionDetails(string tenantId, string sessionId)
        //{
        //    throw new NotImplementedException();
        //}








        // get session analytics. this is used for dashboard controller to show session analytics
        public async Task<SessionAnalyticsResponse> GetSessionAnalytics(string tenantId, DateTime? from, DateTime? to, string? device, string? country)
        {
            var request = new SessionListRequest
            {
                TenantId = tenantId,
                Device = device ?? "",
                Country = country ?? ""
            };

            if (from.HasValue)
                request.From = Timestamp.FromDateTime(from.Value.ToUniversalTime());

            if (to.HasValue)
                request.To = Timestamp.FromDateTime(to.Value.ToUniversalTime());

            try
            {
                return await _client.GetSessionAnalyticsAsync(request);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                // Return empty object instead of throwing
                return new SessionAnalyticsResponse
                {
                    DailySessions = { },
                    DeviceDistribution = { },
                    SessionMetrics = new SessionMetrics
                    {
                        AvgDuration = 0,
                        AvgDurationTrend = 0,
                        BounceRate = 0,
                        BounceRateTrend = 0,
                        AvgActions = 0,
                        AvgActionsTrend = 0
                    }
                };
            }
        }

    }
}
