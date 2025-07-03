//using Microservice.AuthService.Models;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Http;
//using Microsoft.AspNetCore.Mvc;
//using System.Security.Claims;

//namespace Microservice.AuthService.Controllers
//{
//    [Route("api/[controller]")]
//    [ApiController]
//    public class ApiKeyController : ControllerBase
//    {






//        // GET: api/ApiKey
//        [HttpGet("GetApiInfo")]
//        public async Task<IActionResult> GetApiKey()
//        {
//            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
//            if (string.IsNullOrEmpty(userId))
//                return Unauthorized(new { Message = "User not authenticated." });

//            var apiKey = await _apiKeyRepository.GetApiByUserIdAsync(userId);
//            if (apiKey == null)
//                return NotFound(new { Message = "No API key associated with this user." });

//            return Ok(new
//            {
//                apiKey.Domain,
//                apiKey.Org_Name,
//                apiKey.Plan,
//                apiKey.ExpirationDate,
//                apiKey.RequestLimit,
//                apiKey.Created_At
//            });
//        }

//        public class ApiKeyDto
//        {
//            public string Org_Name { get; set; }
//            public string Domain { get; set; }
//            public string Org_Email { get; set; }
//            public string Plan { get; set; }
//        }

//        // POST: api/ApiKey/Create
//        [HttpPost("create")]
//        public async Task<IActionResult> CreateApiKey([FromBody] ApiKeyDto apiKeyDto)
//        {
//            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
//            if (string.IsNullOrEmpty(userId))
//                return Unauthorized(new { Message = "User not authenticated." });

//            var existingKey = await _apiKeyRepository.GetApiByUserIdAsync(userId);
//            if (existingKey != null)
//                return BadRequest(new { Message = "An API key already exists for your account." });

//            var (rawKey, hashedKey) = ApiKeyGenerator.GenerateApiKey();
//            var key = new Tenants
//            {
//                UserId = userId,
//                ApiSecret = hashedKey,
//                Org_Name = apiKeyDto.Org_Name,
//                Domain = apiKeyDto.Domain,
//                Org_Email = apiKeyDto.Org_Email,
//                Plan = apiKeyDto.Plan,
//                Created_At = DateTime.UtcNow,
//                ExpirationDate = DateTime.UtcNow.AddDays(30),
//                RequestLimit = 500,
//                IsRevoked = false
//            };

//            var apiKey = await _apiKeyRepository.CreateApiKeyAsync(key);
//            return Ok(new
//            {
//                Message = "API key created successfully. Please store this key securely.",
//                ApiKey = rawKey
//            });
//        }

//        [HttpPost("regenerate")]
//        public async Task<IActionResult> RegenerateApiKey()
//        {
//            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
//            if (string.IsNullOrEmpty(userId))
//                return Unauthorized(new { Message = "User not authenticated." });

//            var existingKey = await _apiKeyRepository.GetApiByUserIdAsync(userId);
//            if (existingKey == null)
//                return NotFound(new { Message = "No existing API key found to regenerate." });

//            var (rawKey, hashedKey) = ApiKeyGenerator.GenerateApiKey();

//            var newKey = new Tenants
//            {
//                UserId = userId,
//                ApiSecret = hashedKey
//            };

//            var success = await _apiKeyRepository.RegenerateApiKeyAsync(newKey);
//            if (!success)
//                return NotFound(new { Message = "API key already revoked." });

//            return Ok(new
//            {
//                Message = "API key regenerated successfully. Please store this key securely.",
//                RawApiKey = rawKey
//            });
//        }


//        public class Renew
//        {
//            public string Plan { get; set; }
//        }
//        // POST: api/ApiKey/Renew
//        [HttpPost("renew")]
//        public async Task<IActionResult> RenewApiKey([FromBody] Renew renew)
//        {
//            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
//            if (string.IsNullOrEmpty(userId))
//                return Unauthorized(new { Message = "User not authenticated." });

//            var key = await _apiKeyRepository.GetApiByUserIdAsync(userId);
//            if (key == null)
//                return NotFound(new { Message = "API key not found." });

//            // business logic 
//            var update = new Tenants
//            {
//                UserId = key.UserId,
//                Plan = renew.Plan,
//                ExpirationDate = DateTime.UtcNow.AddDays(30),
//                RequestLimit = 500,
//                IsRevoked = false
//            };


//            // business logic 

//            var success = await _apiKeyRepository.RenewApiKeyAsync(update);
//            if (!success)
//                return NotFound(new { Message = "API key not found or already revoked." });

//            return Ok(new { Message = "API key renewed successfully." });
//        }


//        // POST: api/ApiKey/Revoke
//        [Authorize(Roles = "Admin")]
//        [HttpPost("revoke")]
//        public async Task<IActionResult> RevokeApiKey([FromQuery] string userId)
//        {
//            var success = await _apiKeyRepository.RevokeApiKeyAsync(userId);
//            if (!success)
//                return NotFound(new { Message = "API key not found." });

//            return Ok(new { Message = "API key revoked successfully." });
//        }


//    }
//}
