using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microservice.AuthService.Protos;

namespace Microservice.AuthService.Infrastructure.Services
{
    public class ApiKeyGrpcClient
    {
        private readonly ApiKey.ApiKeyClient _client;

        public ApiKeyGrpcClient(IConfiguration config)
        {
            var grpcServerUrl = config["Grpc:ApiKeyServiceUrl"];
            var channel = GrpcChannel.ForAddress(grpcServerUrl);
            _client = new ApiKey.ApiKeyClient(channel);
        }

        public async Task<ApiKeyResponse> GetApiKeyAsync(string userId)
        {
            var request = new ApiKeyRequest { UserId = userId };
            return await _client.GetApiKeyAsync(request);
        }

        public async Task<string> CreateApiKeyAsync(CreateApiKeyRequest request)
        {
            var response = await _client.CreateApiKeyAsync(request);
            return response.ApiHash;
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
    }
}
