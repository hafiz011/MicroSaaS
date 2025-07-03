using Microservice.Session.Infrastructure.GeoIPService;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Models.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Org.BouncyCastle.Utilities.Net;
using System.Net;
using ZstdSharp.Unsafe;

namespace Microservice.Session.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SessionController : ControllerBase
    {
        private readonly IActivityRepository _activityRepository;
        private readonly IApiKeyRepository _apiKeyRepository;
        private readonly GeolocationService _geolocationService;
        private readonly ISessionRepository _sessionRepository;
        private readonly ILogger<SessionController> _logger;

        public SessionController(IActivityRepository activityRepository,
            IApiKeyRepository apiKeyRepository,
            GeolocationService geolocationService,
            ISessionRepository sessionRepository,
            ILogger<SessionController> logger)
        {
            _activityRepository = activityRepository;
            _apiKeyRepository = apiKeyRepository;
            _geolocationService = geolocationService;
            _sessionRepository = sessionRepository;
            _logger = logger;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateSession([FromBody] SessionRequestDto dto)
        {
            try
            {
                if (!Request.Headers.TryGetValue("X-API-KEY", out var rawKey))
                {
                    return Unauthorized("API Key is missing.");
                }
                var isValid = await _apiKeyRepository.TrackUsageAsync(rawKey);
                if (!isValid)
                    return Unauthorized("Invalid API Key");

                var apiKeyInfo = await _apiKeyRepository.GetApiByApiKeyIdAsync(rawKey);

                Request.Headers.TryGetValue("X-SESSION-ID", out var existingSessionId);
                Sessions existingSession = null;
                if (!string.IsNullOrWhiteSpace(existingSessionId))
                {
                    existingSession = await _sessionRepository.GetSessionByIdAsync(existingSessionId);
                }

                if (!string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    var userinfo = await _sessionRepository.GetUserByIdAsync(dto.User_Id);
                    var user = new Users
                    {
                        Tenant_Id = apiKeyInfo.Id,
                        User_Id = dto.User_Id,
                        Name = dto.Name,
                        Email = dto.Email,
                        Last_login = dto.LocalTime,
                        Created_at = DateTime.UtcNow
                    };

                    if (userinfo == null)
                    {
                        await _sessionRepository.CreateUserAsync(user);
                    }
                    else
                    {
                        await _sessionRepository.UpdateUserAsync(user);
                    }
                }

                var location = await _geolocationService.GetGeolocationAsync(dto.Ip_Address);

                // If session exists and anonymous, update it to user
                if (existingSession != null && string.IsNullOrEmpty(existingSession.User_Id) && !string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    var update = new Sessions
                    {
                        Tenant_Id = apiKeyInfo.Id,
                        User_Id = dto.User_Id,
                        Ip_Address = dto.Ip_Address,
                        Local_Time = dto.LocalTime,
                        isActive = true,
                        Geo_Location = new Location
                        {
                            Country = location.Country,
                            City = location.City,
                            Region = location.Region,
                            Postal = location.Postal,
                            Latitude_Longitude = location.Loc,
                            Isp = location.Org,
                            TimeZone = location.TimeZone
                        },
                        Device = new DeviceInfo
                        {
                            Browser = dto.Device.Browser,
                            Fingerprint = dto.Device.Fingerprint,
                            Device_Type = dto.Device.Device_Type,
                            OS = dto.Device.OS,
                            Language = dto.Device.Language,
                            Screen_Resolution = dto.Device.Screen_Resolution
                        }
                    };

                    await _sessionRepository.UpdateSessionAsync(existingSession.Id, update);
                    return Ok(new { SessionsId = existingSession.Id });
                }
                else if (existingSession != null && string.IsNullOrEmpty(existingSession.User_Id) && string.IsNullOrWhiteSpace(dto.User_Id))
                { 
                    return Ok(new { SessionsId = existingSession.Id }); //anonymous user
                }
                else if (existingSession != null && !string.IsNullOrEmpty(existingSession.User_Id) && !string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    return Ok(new { SessionsId = existingSession.Id }); //Pre existing User in Database
                }
                else
                {
                    var session = new Sessions
                    {
                        Tenant_Id = apiKeyInfo.Id,
                        User_Id = dto?.User_Id,
                        Ip_Address = dto.Ip_Address,
                        Local_Time = dto.LocalTime,
                        isActive = true,
                        Geo_Location = new Location
                        {
                            Country = location.Country,
                            City = location.City,
                            Region = location.Region,
                            Postal = location.Postal,
                            Latitude_Longitude = location.Loc,
                            Isp = location.Org,
                            TimeZone = location.TimeZone
                        },
                        Device = new DeviceInfo
                        {
                            Browser = dto.Device.Browser,
                            Fingerprint = dto.Device.Fingerprint,
                            Device_Type = dto.Device.Device_Type,
                            OS = dto.Device.OS,
                            Language = dto.Device.Language,
                            Screen_Resolution = dto.Device.Screen_Resolution
                        }
                    };

                    var SessionsId = await _sessionRepository.CreateSessionAsync(session);
                    return Ok(new { SessionsId = SessionsId.Id }); //create anonymous user

                } 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateSession");
                return StatusCode(500, "Something went wrong.");
            }

        }


        [HttpPost("end-session")]
        public async Task<IActionResult> EndSession()
        {
            try
            {
                if (!Request.Headers.TryGetValue("X-API-KEY", out var rawKey))
                    return Unauthorized("API Key is missing.");

                if (!Request.Headers.TryGetValue("X-SESSION-ID", out var sessionId) || string.IsNullOrEmpty(sessionId))
                    return BadRequest("Session ID is missing.");

                var apiKeyInfo = await _apiKeyRepository.GetApiByApiKeyIdAsync(rawKey);
                if (apiKeyInfo == null)
                    return Unauthorized("Invalid API Key.");

                var session = await _sessionRepository.GetSessionByIdAsync(sessionId);
                if (session == null || session.Tenant_Id != apiKeyInfo.Id)
                    return Unauthorized("Invalid or unauthorized session.");

                if (session.Logout_Time != null)
                    return BadRequest("Session is already ended.");

                await _sessionRepository.EndSessionAsync(sessionId);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending session.");
                return StatusCode(500, "Internal server error while ending session.");
            }
        }



        [HttpPost("log-activity")]
        public async Task<IActionResult> Log([FromBody] ActivityLogDto dto)
        {
            try
            {
                if (!Request.Headers.TryGetValue("X-API-KEY", out var rawKey))
                {
                    return Unauthorized("API Key is missing.");
                }

                if (!Request.Headers.TryGetValue("X-SESSION-ID", out var sessionId) || string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest("Session ID is missing.");
                }

                var apiKeyInfo = await _apiKeyRepository.GetApiByApiKeyIdAsync(rawKey);
                if (apiKeyInfo == null)
                {
                    return Unauthorized("Invalid API Key.");
                }

                var log = new ActivityLog
                {
                    Tenant_Id = apiKeyInfo.Id,
                    Session_Id = sessionId,
                    Activity_Type = dto.Activity_Type,
                    Metadata = dto.Metadata,
                    LocalTime = dto.LocalTime,
                    Time_Stamp = DateTime.UtcNow
                };

                await _activityRepository.CreateLogActivityAsync(log);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, "An error occurred while logging the activity.");
            }
        }


















        [HttpGet("metrics")]
        public async Task<IActionResult> GetSessionMetrics([FromQuery] SessionQueryParams query)
        {
            return Ok();
        }

        [HttpGet("suspicious")]
        public async Task<IActionResult> GetSuspiciousActivities([FromQuery] SessionQueryParams query)
        {
            return Ok();
        }

        [HttpGet("active-users-count")]
        public async Task<IActionResult> GetActiveUserCount([FromQuery] SessionQueryParams query)
        {
            return Ok();
        }

        public class SessionQueryParams
        {
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public string Country { get; set; }
            public string DeviceType { get; set; }
            public bool? SuspiciousOnly { get; set; }
        }



    }
}
