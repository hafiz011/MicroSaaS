using Grpc.Core;

namespace Microservice.Session.Protos
{
    public class ApiKeyService : Session.SessionBase
    {
        // This class is intentionally left empty. It serves as a placeholder for the gRPC service definition.
        // The actual implementation of the service methods will be defined in the derived classes or in the service implementation files.
        // This allows for easy extension and modification of the service without changing the base class.
        // You can add service methods here as needed, or leave it empty if you want to implement them elsewhere.
        // For example, you might implement methods like CreateApiKey, GetApiKey, UpdateApiKey, DeleteApiKey, etc.
        // Each method would typically return a Task of the appropriate response type and accept a request type as a parameter.
        // Example method signature:

        // public override Task<CreateApiKeyResponse> CreateApiKey(CreateApiKeyRequest request, ServerCallContext context)

        public override Task<ApiKeyResponse> ApiKey(ApiKeyRequest request, ServerCallContext context)
        {
            // Simulate DB logic
            Console.WriteLine($"Tracking session for user: {request.UserId}");

            return Task.FromResult(new ApiKeyResponse
            {
                Success = true,
                Message = "Session logged"
            });
        }


    }
}
