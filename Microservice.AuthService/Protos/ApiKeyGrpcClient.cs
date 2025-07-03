using Grpc.Net.Client;
using Microservice.Session.Protos;

namespace Microservice.AuthService.Protos
{
    public class ApiKeyGrpcClient
    {
        private readonly Session.SessionClient _client;

        public ApiKeyGrpcClient()
        {
            var channel = GrpcChannel.ForAddress("http://localhost:7002");
            _client = new Session.SessionClient(channel);
        }


        public async Task<ApiKeyResponse> CreateApiKeyAsync(ApiKeyRequest request, CancellationToken cancellationToken = default)
        {
            return await _client.ApiKeyAsync(request, cancellationToken: cancellationToken);
        }
        public async Task<ApiKeyResponse> GetApiKeyAsync(ApiKeyRequest request, CancellationToken cancellationToken = default)
        {
            return await _client.ApiKeyAsync(request, cancellationToken: cancellationToken);
        }

    }
}
