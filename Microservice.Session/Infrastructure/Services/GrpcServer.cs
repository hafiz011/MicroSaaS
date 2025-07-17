using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Protos;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace Microservice.Session.Infrastructure.Services
{
    public class GrpcServer : ApiKey.ApiKeyBase
    {
        private readonly IApiKeyRepository _apiKeyRepository;
        private readonly IUserInfoRepository _userinfoRepository;

        public GrpcServer(IApiKeyRepository apiKeyRepository)
        {
            _apiKeyRepository = apiKeyRepository;
        }

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


    }
}
