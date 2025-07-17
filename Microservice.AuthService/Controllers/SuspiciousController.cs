using Microservice.AuthService.Entities;
using Microservice.AuthService.Infrastructure.Interfaces;
using Microservice.AuthService.Infrastructure.Services;
using Microservice.AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Microservice.AuthService.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SuspiciousController : ControllerBase
    {
        private readonly ISuspiciousActivityRepository _suspiciousRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly GrpcServiceClient _grpcServiceClient;

        public SuspiciousController(ISuspiciousActivityRepository uspiciousRepository,
            UserManager<ApplicationUser> userManager,
            GrpcServiceClient apiKeyGrpcClient,
            GrpcServiceClient grpcServiceClient)
        {
            _suspiciousRepository = uspiciousRepository;
            _userManager = userManager;
            _grpcServiceClient = grpcServiceClient;
        }


        public class Alert
        {
            public string Country { get; set; }
            public string Device { get; set; }
            public DateTime? From { get; set; }     // Optional override
            public DateTime? To { get; set; }       // Optional override
            public string Range { get; set; }       // "24h", "7d", "30d"
        }


        [HttpGet("alert")]
        public async Task<IActionResult> GetAll([FromQuery] Alert alert)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user.TenantId == null)
                return NotFound(new { Message = "No API key associated with this user." });

            // Handle time range shortcuts
            if (!alert.From.HasValue && !string.IsNullOrWhiteSpace(alert.Range))
            {
                var now = DateTime.UtcNow;

                alert.To = now;
                alert.From = alert.Range switch
                {
                    "24h" => now.AddHours(-24),
                    "7d" => now.AddDays(-7),
                    "30d" => now.AddDays(-30),
                    _ => (DateTime?)null
                };
            }

            var suspicious = await _suspiciousRepository.GetByTenantAsync(
                user.TenantId,
                alert.From,
                alert.To,
                alert.Device,
                alert.Country);

            var dtoList = suspicious.Select(x => new SuspiciousActivityDto
            {
                SessionId = x.SessionId,
                RiskScore = x.RiskScore,
                RiskLevel = x.RiskLevel,
                DetectedAt = x.DetectedAt,
                RiskFactors = x.RiskFactors,
                
            }).ToList();

            return Ok(dtoList);
        }


        // session id details
        [HttpGet("details/{sessionId}")]
        public async Task<IActionResult> GetDetails(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return BadRequest(new { Message = "Suspicious session ID is required." });

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user.TenantId == null)
                return NotFound(new { Message = "No API key associated with this user." });

            var suspicious = await _suspiciousRepository.GetBySessionIdAsync(user.TenantId, sessionId);
            if (suspicious == null)
                return NotFound(new { Message = "Suspicious session not found." });

            var userinfo = await _grpcServiceClient.GetUserInfo(suspicious.UserId, suspicious.TenantId);

            // Optional: map to DTO
            var dto = new SuspiciousActivityDto
            {
                SessionId = suspicious.SessionId,
                UserName = userinfo.UserName,
                UserEmail = userinfo.UserEmail,
                IpAddress = suspicious.IpAddress,
                LoginTime = suspicious.LoginTime.ToString(),
                RiskScore = suspicious.RiskScore,
                RiskLevel = suspicious.RiskLevel,
                DetectedAt = suspicious.DetectedAt,
                RiskFactors = suspicious.RiskFactors,
                Browser = suspicious.Device.Browser,
                DeiceType = suspicious.Device.Device_Type,
                OS = suspicious.Device.OS,
                Language = suspicious.Device.Language,
                ScreenResolution = suspicious.Device.Screen_Resolution,
                Country = suspicious.Geo_Location.Country,
                City = suspicious.Geo_Location.City,
                Region = suspicious.Geo_Location.Region,
                Postal = suspicious.Geo_Location.Postal,
                LatitudeLongitude= suspicious.Geo_Location.Latitude_Longitude,
                TimeZone = suspicious.Geo_Location.TimeZone,
                Isp = suspicious.Geo_Location.Isp,
                is_vpn = suspicious.Geo_Location.is_vpn,
            };

            return Ok(dto);
        }


        // update suspicious to safe session
        [HttpPut("{suspiciousId}")]
        public async Task<IActionResult> Update(string suspiciousId)
        {
            if (string.IsNullOrWhiteSpace(suspiciousId))
                return BadRequest(new { Message = "Suspicious session ID is required." });

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user.TenantId == null)
                return NotFound(new { Message = "No API key associated with this user." });

            await _suspiciousRepository.UpdateSuspiciousStatusAsync(user.TenantId, suspiciousId);
            return Ok(new { Message = "Suspicious session marked as safe.", SessionId = suspiciousId });
        }


        public class SuspiciousActivityDto
        {
            public string SessionId { get; set; }
            public string UserName { get; set; }
            public string UserEmail { get; set; }
            public string IpAddress { get; set; }
            public string LoginTime { get; set; }
            public double RiskScore { get; set; }
            public string RiskLevel { get; set; }
            public DateTime DetectedAt { get; set; }
            public List<string> RiskFactors { get; set; }

            // device info
            public string Browser { get; set; }
            public string DeiceType { get; set; }
            public string OS { get; set; }
            public string Language { get; set; }
            public string ScreenResolution { get; set; }

            // location info
            public string Country { get; set; }
            public string City { get; set; }
            public string Region { get; set; }
            public string Postal { get; set; }
            public string LatitudeLongitude { get; set; }
            public string TimeZone { get; set; }
            public string Isp { get; set; }
            public bool is_vpn { get; set; }
        }

    }
}
