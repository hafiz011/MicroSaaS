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
            public string ReferrerUrl { get; set; }
            public DeviceInfoDto Device { get; set; }
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
                //string apiKey = Request.Headers["X-API-KEY"].FirstOrDefault() ?? Request.Query["apiKey"];
                //string sessionId = Request.Headers["X-SESSION-ID"].FirstOrDefault() ?? Request.Query["sessionId"];

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
                    existingSession = await _sessionRepository.GetSessionByIdAsync(existingSessionId, apiKeyInfo.Id); // get session if it already exits
                }

                // create user or update user
                if (!string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    // dto user id not null
                    var userinfo = await _userInfoRepository.getUserById(dto.User_Id, apiKeyInfo.Id);  // get user if it exits in DB
                    var user = new Users
                    {
                        Tenant_Id = apiKeyInfo.Id,
                        User_Id = dto.User_Id,
                        Name = dto.Name,
                        Email = dto.Email,
                        Last_login = DateTime.UtcNow
                    };

                    if (userinfo == null) //if user is null than create user
                    {
                        user.Created_at = DateTime.UtcNow;
                        await _userInfoRepository.CreateUserAsync(user);
                    }
                    else
                    {
                        await _userInfoRepository.UpdateUserAsync(user); // if user is exits than update login time
                    }
                }

                var location = await _geolocationService.GetGeolocationAsync(dto.Ip_Address); // get location using ip address

                // Condition 1: Anonymous session gets associated with a logged-in user
                // If the session exists AND the session's User_Id is null (anonymous)
                // AND the request contains a valid User_Id,
                // then update the session to link it to the logged-in user.
                // update session for anonymous to loin user
                if (existingSession != null && string.IsNullOrEmpty(existingSession.User_Id) && !string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    var update = new Sessions
                    {
                        Tenant_Id = existingSession.Tenant_Id,
                        Id = existingSession.Id,
                        User_Id = dto.User_Id,
                        isActive = true,
                        Logout_Time = null,
                    };
                    await _sessionRepository.UpdateSessionAsync(update);
                    // RabbitMQ 
                    _publisher.PublishSessionRiskCheck(new SessionRiskCheckMessage
                    {
                        SessionId = existingSession.Id,
                        TenantId = apiKeyInfo.Id,
                        UserId = dto.User_Id,
                        Email = dto.Email,
                        Ip_Address = dto.Ip_Address,
                        Cliend_Domaim = apiKeyInfo.Domain,
                        Login_Time = DateTime.UtcNow,
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
                    return Ok(new { SessionsId = existingSession.Id });
                }

                // Condition 2: Anonymous session revisited without login
                // If the session exists AND the session's User_Id is null (anonymous)
                // AND the request also has no User_Id (still anonymous),
                // then just return the existing session ID without updating anything.
                else if (existingSession != null && string.IsNullOrEmpty(existingSession.User_Id) && string.IsNullOrWhiteSpace(dto.User_Id))
                { 
                    return Ok(new { SessionsId = existingSession.Id }); // Condition 2: Anonymous → Return visit (no change)
                }

                // Condition 3: Returning user session
                // If the session exists AND it already has a User_Id
                // AND the incoming request also has the same User_Id,
                // then the user is returning, so just return the existing session ID.
                else if (existingSession != null && !string.IsNullOrEmpty(existingSession.User_Id) && !string.IsNullOrWhiteSpace(dto.User_Id) && existingSession.User_Id == dto.User_Id)
                {
                    return Ok(new { SessionsId = existingSession.Id }); // User already exists → return
                }
                //create session
                else
                {
                    var session = new Sessions
                    {
                        Tenant_Id = apiKeyInfo.Id,
                        User_Id = dto?.User_Id,
                        Ip_Address = dto.Ip_Address,
                        isActive = true,
                        Login_Time = DateTime.UtcNow,
                        ReferrerUrl = dto.ReferrerUrl,

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
                            Email = dto.Email,
                            Ip_Address = dto.Ip_Address,
                            Cliend_Domaim = apiKeyInfo.Domain,
                            Login_Time = DateTime.UtcNow,
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

                var session = await _sessionRepository.GetSessionByIdAsync(sessionId, apiKeyInfo.Id);  // check session in database
                if (session == null || session.Tenant_Id != apiKeyInfo.Id)
                    return Unauthorized("Invalid or unauthorized session.");

                await _sessionRepository.EndSessionAsync(sessionId);  // end session

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending session.");
                return StatusCode(500, "Internal server error while ending session.");
            }
        }



        public class ActivityLogDto
        {
            public string User_Id { get; set; }
            public string Action_Type { get; set; }     // view_product, add_to_cart, checkout, payment, etc.
            public string Product_Id { get; set; }
            public string Category_Id { get; set; }
            public int Quantity { get; set; }
            public double Price { get; set; }
            public string Url { get; set; }
            public string ReferrerUrl { get; set; }
            public Dictionary<string, string> Metadata { get; set; }
            public string Request_Method { get; set; }
            public int Response_Code { get; set; }
            public bool Success_Flag { get; set; }
            public double Response_Time { get; set; }
            public DateTime Time_Stamp { get; set; }
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
                var sessiondata = await _sessionRepository.GetSessionByIdAsync(sessionId, apiKeyInfo.Id);

                if (sessiondata != null)
                {
                    if (sessiondata.isActive == false)
                    {
                        var update = new Sessions
                        {   
                            Tenant_Id = sessiondata.Tenant_Id,
                            Id = sessiondata.Id,
                            User_Id = sessiondata.User_Id,
                            isActive = true,
                            Logout_Time = null
                        };
                        await _sessionRepository.UpdateSessionAsync(update);
                    }

                    // Activity Log create
                    var log = new ActivityLog
                    {
                        Tenant_Id = apiKeyInfo.Id,
                        Session_Id = sessionId,
                        User_Id = sessiondata.User_Id,
                        Action_Type = dto.Action_Type,
                        Product_Id = dto.Product_Id,
                        Category_Id = dto.Category_Id,
                        Quantity = dto.Quantity,
                        Price = dto.Price,
                        Url = dto.Url,
                        ReferrerUrl = dto.ReferrerUrl,
                        Metadata = dto.Metadata,
                        Request_Method = dto.Request_Method,
                        Response_Code = dto.Response_Code,
                        Success_Flag = dto.Success_Flag,
                        Response_Time = dto.Response_Time,
                        Time_Stamp = DateTime.UtcNow
                    };

                    await _activityRepository.CreateLogActivityAsync(log);
                }
                else
                {
                    return BadRequest("Session ID is miss match."); //get session id
                }

                return Ok("Activity logged successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while logging the activity: {ex.Message}");
            }
        }






    }
}
