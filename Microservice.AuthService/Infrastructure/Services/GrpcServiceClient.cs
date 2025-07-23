using Google.Protobuf.WellKnownTypes;
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

        // api key grpc serivice
        public async Task<ApiKeyResponse> GetApiKeyAsync(string userId)
        {
            var request = new ApiKeyRequest { UserId = userId };
            return await _client.GetApiKeyAsync(request);
        }

        public async Task<(string ApiHash, string TenantId)> CreateApiKeyAsync(CreateApiKeyRequest request)
        {
            var response = await _client.CreateApiKeyAsync(request);
            return (response.ApiHash, response.TanantId);
        }

        public async Task<string> RegenerateApiKeyAsync(string userId)
        {
            var response = await _client.RegenerateApiKeyAsync(new ApiKeyRequest { UserId = userId });
            return response.ApiHash;
        }

        public async Task<ApiKeyResponse> RenewApiKeyAsync(RenewApiKeyRequest request)
        {
            return await _client.RenewApiKeyAsync(request);
        }

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

        // active user session list
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

            return await _client.GetSessionListAsync(request);
        }


    }
}
