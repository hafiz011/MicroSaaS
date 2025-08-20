using Grpc.Core;
using Microservice.AuthService.Entities;
using Microservice.AuthService.Infrastructure.Interfaces;
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
    public class DashboardController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly GrpcServiceClient _grpcServiceClient;
        private readonly ISuspiciousActivityRepository _suspiciousRepository;

        public DashboardController(UserManager<ApplicationUser> userManager,
            GrpcServiceClient grpcServiceClient,
            ISuspiciousActivityRepository suspiciousActivityRepository)
        {
            _userManager = userManager;
            _grpcServiceClient = grpcServiceClient;
            _suspiciousRepository = suspiciousActivityRepository;
        }

        //// Active session/user list
        //[HttpGet("ActiveUsers")]
        // public async Task<IActionResult> ActiveUsers([FromQuery] Query query)
        // {
        //     var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        //     if (string.IsNullOrEmpty(userId))
        //         return Unauthorized(new { Message = "User not authenticated." });

        //     var user = await _userManager.FindByIdAsync(userId);
        //     if (user?.TenantId == null)
        //         return NotFound(new { Message = "No API key associated with this user." });

        //     try
        //     {
        //         var sessionResponse = await _grpcServiceClient.GetSessionList(
        //             user.TenantId,
        //             query.From,
        //             query.To,
        //             query.Device,
        //             query.Country
        //         );

        //         // যদি কোনো session না থাকে → empty response return করো
        //         if (sessionResponse == null || !sessionResponse.Sessions.Any())
        //         {
        //             return Ok(new
        //             {
        //                 Sessions = new List<object>(),
        //                 TopUsers = new List<object>()
        //             });
        //         }

        //         // Active Sessions
        //         var activeSessions = sessionResponse.Sessions
        //             .Where(s => s.Status == "Active")
        //             .Select(s => new
        //             {
        //                 s.UserName,
        //                 s.Email,
        //                 s.UserId,
        //                 s.IpAddress,
        //                 s.City,
        //                 s.Country,
        //                 s.Status,
        //                 s.DeviceType,
        //                 s.LoginTime,
        //                 s.Lac,
        //                 s.Sessionid
        //             })
        //             .ToList();

        //         // Top Users
        //         var topUsers = sessionResponse.Sessions
        //              .Where(s => !string.IsNullOrEmpty(s.UserId))
        //              .GroupBy(s => s.UserId)
        //              .Select(g => new
        //              {
        //                  UserId = g.Key,
        //                  UserName = g.Select(x => x.UserName).FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? string.Empty,
        //                  Email = g.Select(x => x.Email).FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? string.Empty,
        //                  Sessions = g.Count(),
        //                  Actions = g.Sum(x => x.Action)
        //              })
        //              .OrderByDescending(u => u.Actions)
        //              .Take(20)
        //              .ToList();


        //         return Ok(new
        //         {
        //             Session = activeSessions,
        //             TopUser = topUsers
        //         });
        //     }
        //     catch (RpcException rpcEx)
        //     {
        //         return StatusCode(502, new { Message = $"Active service unavailable. ({rpcEx.StatusCode})" });
        //     }
        //     catch (Exception ex)
        //     {
        //         return StatusCode(500, new { Message = "An error occurred while fetching active sessions.", Details = ex.Message });
        //     }
        // }

        [HttpGet("ActiveUsers")]
        public async Task<IActionResult> ActiveUsers([FromQuery] Query query)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user.TenantId == null)
                return NotFound(new { Message = "No API key associated with this user." });

            try
            {
                var sessions = await _grpcServiceClient.GetSessionList(
                    user.TenantId,
                    query.From,
                    query.To,
                    query.Device,
                    query.Country
                );

                // Always return 200 OK with sessions (could be empty)
                return Ok(sessions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An error occurred while fetching active sessions." });
            }
        }


        // active user details
        //[HttpGet("ActiveUserDetails")]
        //public async Task<IActionResult> ActiveUserDetails(string sessionId)
        //{
        //    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        //    if (string.IsNullOrEmpty(userId))
        //        return Unauthorized(new { Message = "User not authenticated." });

        //    var user = await _userManager.FindByIdAsync(userId);
        //    if (user.TenantId == null)
        //        return NotFound(new { Message = "No API key associated with this user." });

        //    var details = await _grpcServiceClient.GetSessionDetails(user.TenantId, sessionId);
        //    if (details == null)
        //        return NotFound();

        //    return Ok(details);

        //}


        // suspicious activity list
        [HttpGet("alert")]
        public async Task<IActionResult> GetAll([FromQuery] Query alert)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user.TenantId == null)
                return NotFound(new { Message = "No API key associated with this user." });

            var suspicious = await _suspiciousRepository.GetByTenantAsync(
                user.TenantId,
                alert.From,
                alert.To,
                alert.Device,
                alert.Country);

            // Total count of suspicious activities
            var totalCount = suspicious.Count;

            var dtoList = suspicious.Select(suspicious => new SuspiciousActivityDto
            {
                SessionId = suspicious.SessionId,
                UserEmail = suspicious.Email,
                IpAddress = suspicious.IpAddress,
                LoginTime = suspicious.LoginTime.ToString(),
                DetectedAt = suspicious.DetectedAt,
                RiskLevel = suspicious.RiskLevel,
                RiskFactors = suspicious.RiskFactors,
                Browser = suspicious.Device.Browser,
                DeviceType = suspicious.Device.Device_Type,
                OS = suspicious.Device.OS,
                Country = suspicious.Geo_Location.Country,
                Is_Suspicious = suspicious.IsSuspicious,
            }).ToList();

            //   return Ok(dtoList, totalCount);
            return Ok(new
            {
                TotalSuspicious = totalCount,
                SuspiciousActivities = dtoList
            });
        }


        // suspicious session id details
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

          //  var userinfo = await _grpcServiceClient.GetUserInfo(suspicious.UserId, suspicious.TenantId);

            // Optional: map to DTO
            var dto = new SuspiciousActivityDto
            {
                SessionId = suspicious.SessionId,
                UserEmail = suspicious.Email,
                IpAddress = suspicious.IpAddress,
                LoginTime = suspicious.LoginTime.ToString(),
                RiskLevel = suspicious.RiskLevel,
                DetectedAt = suspicious.DetectedAt,
                RiskFactors = suspicious.RiskFactors,
                Browser = suspicious.Device.Browser,
                DeviceType = suspicious.Device.Device_Type,
                OS = suspicious.Device.OS,
                Country = suspicious.Geo_Location.Country,
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
            public int Count { get; set; }
            public string SessionId { get; set; }
            public string UserEmail { get; set; }
            public string IpAddress { get; set; }
            public string LoginTime { get; set; }
            public string RiskLevel { get; set; }
            public DateTime DetectedAt { get; set; }
            public List<string> RiskFactors { get; set; }

            public string Browser { get; set; }
            public string DeviceType { get; set; }
            public string OS { get; set; }
            public string Country { get; set; }
            public bool Is_Suspicious { get; set; }
        }



        /// <summary>
        /// Get session overview analytics data.
        /// Retrieves daily sessions trend, device distribution, and session metrics.
        /// Data is fetched from gRPC service or database.
        /// Returns a <see cref="SessionAnalyticsResponse"/> object containing
        /// all chart data required for the analytics dashboard.
        /// </summary>

        [HttpGet("analytics")]
        public async Task<IActionResult> GetSessionOverview([FromQuery] Query query)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { Message = "User not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user.TenantId == null)
                return NotFound(new { Message = "No API key associated with this user." });

            try
            {
                // Fetch session analytics data from gRPC service
                var response = await _grpcServiceClient.GetSessionAnalytics(
                    user.TenantId,
                    query.From,
                    query.To,
                    query.Device,
                    query.Country
                );

                // Map gRPC response to API DTO (camelCase for frontend)
                var result = new
                {
                    dailySessions = response.DailySessions.Select(ds => new {
                        date = ds.Date,
                        sessions = ds.Sessions,
                        suspicious = ds.Suspicious
                    }),
                    deviceDistribution = response.DeviceDistribution.Select(dm => new {
                        name = dm.Name,
                        total = dm.Total,
                        avgDuration = dm.AvgDuration,
                        avgActions = dm.AvgActions
                    }),
                    sessionMetrics = new
                    {
                        avgDuration = response.SessionMetrics.AvgDuration,
                        avgDurationTrend = response.SessionMetrics.AvgDurationTrend,
                        bounceRate = response.SessionMetrics.BounceRate,
                        bounceRateTrend = response.SessionMetrics.BounceRateTrend,
                        avgActions = response.SessionMetrics.AvgActions,
                        avgActionsTrend = response.SessionMetrics.AvgActionsTrend
                    }
                };

                return Ok(result);
            }
            catch (RpcException rpcEx)
            {
                return StatusCode(502, new { Message = "Analytics service unavailable." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An error occurred while fetching analytics data." });
            }
        }




    }
}
