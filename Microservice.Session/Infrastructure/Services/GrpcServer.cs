using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Protos;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http;
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

        // api key info
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

        // create api key
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

        // regenerate api key
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

        // renew api key
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

        // revike api key
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

        // user info
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

        // active session and user list
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
                    Region = session.Geo_Location?.Region ?? "",
                    Status = session.isActive ? "Active" : "Inactive",
                    DeviceOs = session.Device?.OS ?? "",
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


    }
}
