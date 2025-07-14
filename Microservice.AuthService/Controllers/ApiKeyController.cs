using Grpc.Core;
using Microservice.AuthService.Infrastructure.Services;
using Microservice.AuthService.Models;
using Microservice.AuthService.Protos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Microservice.AuthService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ApiKeyController : ControllerBase
    {

        private readonly UserManager<ApplicationUser> _userManager;

        private readonly ApiKeyGrpcClient _apiKeyGrpcClient;

        public ApiKeyController(UserManager<ApplicationUser> userManager, ApiKeyGrpcClient apiKeyGrpcClient)
        {
            _userManager = userManager;
            _apiKeyGrpcClient = apiKeyGrpcClient;
        }


        // GET: api/ApiKey/GetApiInfo
        [HttpGet("GetApiInfo")]
        public async Task<IActionResult> GetApiKey()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            try
            {
                var apiKey = await _apiKeyGrpcClient.GetApiKeyAsync(userId);

                if (apiKey == null)
                    return NotFound(new { Message = "No API key associated with this user." });

                return Ok(new
                {
                    apiKey.Domain,
                    OrgName = apiKey.OrgName,  // fixed case-sensitive property
                    apiKey.Plan,
                    ExpirationDate = apiKey.ExpirationDate.ToDateTime(),
                    apiKey.RequestLimit,
                    CreatedAt = apiKey.CreatedAt.ToDateTime()

                });
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return NotFound(new { Message = $"No API key found for user {userId}." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An error occurred while fetching the API key."});
            }
        }


        public class ApiKeyDto
        {
            public string Org_Name { get; set; }
            public string Domain { get; set; }
            public string Org_Email { get; set; }
            public string Plan { get; set; }
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateApiKey([FromBody] ApiKeyDto apiKeyDto)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { Message = "User not found." });

            try
            {
                var request = new CreateApiKeyRequest
                {
                    UserId = userId,
                    OrgName = apiKeyDto.Org_Name,
                    Domain = apiKeyDto.Domain,
                    OrgEmail = apiKeyDto.Org_Email,
                    Plan = apiKeyDto.Plan,
                    ExpirationDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddDays(30)),
                    CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                    RequestLimit = 500
                };

                var response = await _apiKeyGrpcClient.CreateApiKeyAsync(request);
                user.TenantId = response.TenantId;
                var update = await _userManager.UpdateAsync(user);

                return Ok(new
                {
                    Message = "API key created successfully. Please store this key securely.",
                    ApiKey = response.ApiHash
                });
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.AlreadyExists)
            {
                return BadRequest(new { Message = "An API key already exists for your account." });
            }
        }

        [HttpPost("regenerate")]
        public async Task<IActionResult> RegenerateApiKey()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            try
            {
                var response = await _apiKeyGrpcClient.RegenerateApiKeyAsync(userId);
                return Ok(new
                {
                    Message = "API key regenerated successfully. Please store this key securely.",
                    RawApiKey = response
                });
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return NotFound(new { Message = "No existing API key found to regenerate." });
            }
        }


        public class Renew
        {
            public string Plan { get; set; }
        }

        [HttpPost("renew")]
        public async Task<IActionResult> RenewApiKey([FromBody] Renew renew)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            try
            {
                var request = new RenewApiKeyRequest
                {
                    UserId = userId,
                    Plan = renew.Plan,
                    RequestLimit = 500,
                    ExpirationDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddDays(30)),
                    IsRevoked = false
                };

                var result = await _apiKeyGrpcClient.RenewApiKeyAsync(request);
                return Ok(new
                {
                    Message = "API key renewed successfully.",
                    Plan = result.Plan,
                    ExpirationDate = result.ExpirationDate.ToDateTime(),
                    RequestLimit = result.RequestLimit
                });
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return NotFound(new { Message = "API key not found." });
            }
        }


        [Authorize(Roles = "Admin")]
        [HttpPost("revoke")]
        public async Task<IActionResult> RevokeApiKey([FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest(new { Message = "UserId is required." });

            try
            {
                var result = await _apiKeyGrpcClient.RevokeApiKeyAsync(userId);
                return Ok(new
                {
                    Message = "API key revoked successfully.",
                    IsRevoked = result.IsRevoked
                });
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return NotFound(new { Message = "API key not found." });
            }
        }


    }
}
