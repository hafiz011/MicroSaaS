using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.GeoIPService;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Models.DTOs;
using Microsoft.AspNetCore.Mvc;

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
        private readonly IUserInfoRepository _userInfoRepository;
        private IRabbitMqPublisher _publisher;
        private readonly ILogger<SessionController> _logger;

        public SessionController(IActivityRepository activityRepository,
            IApiKeyRepository apiKeyRepository,
            GeolocationService geolocationService,
            ISessionRepository sessionRepository,
            IUserInfoRepository userInfoRepository,
            IRabbitMqPublisher rabbitMqPublisher,
            ILogger<SessionController> logger)
        {
            _activityRepository = activityRepository;
            _apiKeyRepository = apiKeyRepository;
            _geolocationService = geolocationService;
            _sessionRepository = sessionRepository;
            _userInfoRepository = userInfoRepository;
            _publisher = rabbitMqPublisher;
            _logger = logger;
        }


        public class SessionRequestDto
        {
            public string User_Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string Ip_Address { get; set; }
            public DeviceInfoDto Device { get; set; }
            public DateTime LocalTime { get; set; }
        }

        public class DeviceInfoDto
        {
            public string Fingerprint { get; set; }
            public string Browser { get; set; }
            public string Device_Type { get; set; }
            public string OS { get; set; }
            public string Language { get; set; }
            public string Screen_Resolution { get; set; }
        }


        [HttpPost("create")]
        public async Task<IActionResult> CreateSession([FromBody] SessionRequestDto dto)
        {
            try
            {
                if (!Request.Headers.TryGetValue("X-API-KEY", out var rawKey))  //get api key hash
                {
                    return Unauthorized("API Key is missing."); 
                }
                var isValid = await _apiKeyRepository.TrackUsageAsync(rawKey);   // track apk key using limit and validation in DB
                if (!isValid)
                    return Unauthorized("Invalid API Key");

                var apiKeyInfo = await _apiKeyRepository.GetApiByApiKeyIdAsync(rawKey); // check info API key in DB

                Request.Headers.TryGetValue("X-SESSION-ID", out var existingSessionId); // get session id
                Sessions existingSession = null;
                if (!string.IsNullOrWhiteSpace(existingSessionId))
                {
                    existingSession = await _sessionRepository.GetSessionByIdAsync(existingSessionId); // get session id if it already exits
                }

                if (!string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    var userinfo = await _userInfoRepository.GetUserByIdAsync(dto.User_Id);  // get user if it exits in DB
                    var user = new Users
                    {
                        Tenant_Id = apiKeyInfo.Id,
                        User_Id = dto.User_Id,
                        Name = dto.Name,
                        Email = dto.Email,
                        Last_login = dto.LocalTime,
                        Created_at = DateTime.UtcNow
                    };

                    if (userinfo == null) //if user is null than create user
                    {
                        await _userInfoRepository.CreateUserAsync(user);
                    }
                    else
                    {
                        await _userInfoRepository.UpdateUserAsync(user); // if user is exits than update login time
                    }
                }

                var location = await _geolocationService.GetGeolocationAsync(dto.Ip_Address); // get location using ip address

                // Condition 1: Anonymous → Login → Update session
                if (existingSession != null && string.IsNullOrEmpty(existingSession.User_Id) && !string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    var update = new Sessions
                    {
                        Tenant_Id = apiKeyInfo.Id,
                        User_Id = dto.User_Id,
                        Ip_Address = dto.Ip_Address,
                        Local_Time = dto.LocalTime,
                        isActive = true,
                        Geo_Location = new Entities.Location
                        {
                            Country = location.Country,
                            City = location.City,
                            Region = location.Region,
                            Postal = location.Postal,
                            Latitude_Longitude = location.Loc,
                            Isp = location.Org,
                            TimeZone = location.TimeZone
                        },
                        Device = new Entities.DeviceInfo
                        {
                            Browser = dto.Device.Browser,
                            Fingerprint = dto.Device.Fingerprint,
                            Device_Type = dto.Device.Device_Type,
                            OS = dto.Device.OS,
                            Language = dto.Device.Language,
                            Screen_Resolution = dto.Device.Screen_Resolution
                        }
                    };


                    _publisher.PublishSessionRiskCheck(new SessionRiskCheckMessage
                    {
                        SessionId = existingSession.Id,
                        TenantId = apiKeyInfo.Id,
                        UserId = dto.User_Id,
                        Ip_Address = dto.Ip_Address,
                        Local_Time = dto.LocalTime,
                        Geo_Location = new Models.DTOs.Location
                        {
                            Country = location.Country,
                            City = location.City,
                            Region = location.Region,
                            Postal = location.Postal,
                            Latitude_Longitude = location.Loc,
                            Isp = location.Org,
                            TimeZone = location.TimeZone
                        },
                        Device = new Models.DTOs.DeviceInfo
                        {
                            Browser = dto.Device.Browser,
                            Fingerprint = dto.Device.Fingerprint,
                            Device_Type = dto.Device.Device_Type,
                            OS = dto.Device.OS,
                            Language = dto.Device.Language,
                            Screen_Resolution = dto.Device.Screen_Resolution
                        }
                    });

                    await _sessionRepository.UpdateSessionAsync(existingSession.Id, update);
                    return Ok(new { SessionsId = existingSession.Id });
                }
                else if (existingSession != null && string.IsNullOrEmpty(existingSession.User_Id) && string.IsNullOrWhiteSpace(dto.User_Id))
                { 
                    return Ok(new { SessionsId = existingSession.Id }); // Condition 2: Anonymous → Return visit (no change)
                }
                else if (existingSession != null && !string.IsNullOrEmpty(existingSession.User_Id) && !string.IsNullOrWhiteSpace(dto.User_Id) && existingSession.User_Id == dto.User_Id)
                {
                    return Ok(new { SessionsId = existingSession.Id }); // Condition 3: User already exists → return
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
                        Geo_Location = new Entities.Location
                        {
                            Country = location.Country,
                            City = location.City,
                            Region = location.Region,
                            Postal = location.Postal,
                            Latitude_Longitude = location.Loc,
                            Isp = location.Org,
                            TimeZone = location.TimeZone
                        },
                        Device = new Entities.DeviceInfo
                        {
                            Browser = dto.Device.Browser,
                            Fingerprint = dto.Device.Fingerprint,
                            Device_Type = dto.Device.Device_Type,
                            OS = dto.Device.OS,
                            Language = dto.Device.Language,
                            Screen_Resolution = dto.Device.Screen_Resolution
                        }
                    };

                    var sessionCreated = await _sessionRepository.CreateSessionAsync(session);

                    if (!string.IsNullOrWhiteSpace(dto.User_Id))
                    {
                        _publisher.PublishSessionRiskCheck(new SessionRiskCheckMessage
                        {
                            SessionId = sessionCreated.Id,
                            TenantId = apiKeyInfo.Id,
                            UserId = dto.User_Id,
                            Ip_Address = dto.Ip_Address,
                            Local_Time = dto.LocalTime,
                            Geo_Location = new Models.DTOs.Location
                            {
                                Country = location.Country,
                                City = location.City,
                                Region = location.Region,
                                Postal = location.Postal,
                                Latitude_Longitude = location.Loc,
                                Isp = location.Org,
                                TimeZone = location.TimeZone
                            },
                            Device = new Models.DTOs.DeviceInfo
                            {
                                Browser = dto.Device.Browser,
                                Fingerprint = dto.Device.Fingerprint,
                                Device_Type = dto.Device.Device_Type,
                                OS = dto.Device.OS,
                                Language = dto.Device.Language,
                                Screen_Resolution = dto.Device.Screen_Resolution
                            }
                        });
                    }
             
                    return Ok(new { SessionsId = sessionCreated.Id }); //create anonymous user

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
                if (!Request.Headers.TryGetValue("X-API-KEY", out var rawKey))   //get api key hash
                    return Unauthorized("API Key is missing.");

                if (!Request.Headers.TryGetValue("X-SESSION-ID", out var sessionId) || string.IsNullOrEmpty(sessionId))  //get session id
                    return BadRequest("Session ID is missing.");

                var apiKeyInfo = await _apiKeyRepository.GetApiByApiKeyIdAsync(rawKey);  // check info API key in DB
                if (apiKeyInfo == null)
                    return Unauthorized("Invalid API Key.");

                var session = await _sessionRepository.GetSessionByIdAsync(sessionId);  // check session in database
                if (session == null || session.Tenant_Id != apiKeyInfo.Id)
                    return Unauthorized("Invalid or unauthorized session.");

                if (session.Logout_Time != null)
                    return BadRequest("Session is already ended.");

                await _sessionRepository.EndSessionAsync(sessionId);  // end session

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
                    return Unauthorized("API Key is missing."); //get api key
                }

                if (!Request.Headers.TryGetValue("X-SESSION-ID", out var sessionId) || string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest("Session ID is missing."); //get session id
                }

                var apiKeyInfo = await _apiKeyRepository.GetApiByApiKeyIdAsync(rawKey); // check info API key in DB
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

                await _activityRepository.CreateLogActivityAsync(log); // insert event log in DB
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, "An error occurred while logging the activity.");
            }
        }



        //private SessionRiskCheckMessage BuildRiskMessage(SessionRiskCheckMessage session, string tenantId)
        //{
        //    return new SessionRiskCheckMessage
        //    {
        //        SessionId = session.SessionId,
        //        TenantId = tenantId,
        //        UserId = session.User_Id,
        //        Ip_Address = session.Ip_Address,
        //        Local_Time = session.Local_Time,
        //        Geo_Location = new Location { ... },
        //        Device = new DeviceInfo { ... }
        //    };
        //}











        //[HttpGet("suspicious/details")]
        //public async Task<IActionResult> GetSuspiciousWithDetails([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        //{
        //    var tenantId = await GetTenantFromApiKey();
        //    var data = await _suspiciousActivityRepository.GetSuspiciousWithSessionDetailsAsync(tenantId, from, to);
        //    return Ok(data);
        //}







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
