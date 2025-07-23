using Microservice.AuthService.Entities;
using Microservice.AuthService.Infrastructure.Services;
using Microservice.AuthService.Models;
using Microservice.AuthService.Models.DashboardDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Microservice.AuthService.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SessionController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly GrpcServiceClient _grpcServiceClient;

        public SessionController(UserManager<ApplicationUser> userManager, GrpcServiceClient grpcServiceClient)
        {
            _userManager = userManager;
            _grpcServiceClient = grpcServiceClient;
        }

        [HttpGet("ActiveUsers")]
        public async Task<IActionResult> ActiveUsers([FromQuery] Query query)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user.TenantId == null)
                return NotFound(new { Message = "No API key associated with this user." });

            // Handle time range shortcuts
            if (!query.From.HasValue && !string.IsNullOrWhiteSpace(query.Range))
            {
                var now = DateTime.UtcNow;
                query.To = now;
                query.From = query.Range switch
                {
                    "24h" => now.AddHours(-24),
                    "7d" => now.AddDays(-7),
                    "30d" => now.AddDays(-30),
                    _ => (DateTime?)null
                };
            }

            var suspicious = await _grpcServiceClient.GetSessionList(
                user.TenantId,
                query.From,
                query.To,
                query.Device,
                query.Country);

            if (suspicious == null)
                return NotFound();



            return Ok(suspicious);
        }

    }
}
