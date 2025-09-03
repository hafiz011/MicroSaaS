using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.GeoIPService;
using Microservice.Session.Infrastructure.Interfaces;
using Microservice.Session.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.RegularExpressions;

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


        /// <summary>
        /// Data Transfer Object for session requests
        /// </summary>
        public class SessionRequestDto
        {
            public string User_Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string Ip_Address { get; set; }
            public string ReferrerUrl { get; set; }
            public DeviceDto Device { get; set; }
        }

        /// <summary>
        /// Data Transfer Object for device information
        /// </summary>
        public class DeviceDto
        {
            public string Fingerprint { get; set; }
            public string UserAgent { get; set; }
            public string Language { get; set; }
            public string Screen_Resolution { get; set; }
        }

        /// <summary>
        /// Creates or updates a user session
        /// </summary>
        /// <param name="dto">Session request data</param>
        /// <returns>Action result with session ID</returns>
        [HttpPost("create")]
        public async Task<IActionResult> CreateSession([FromBody] SessionRequestDto dto)
        {
            try
            {
                //        //string apiKey = Request.Headers["X-API-KEY"].FirstOrDefault() ?? Request.Query["apiKey"];
                //        //string sessionId = Request.Headers["X-SESSION-ID"].FirstOrDefault() ?? Request.Query["sessionId"];

                //  #region Input Validation
                if (dto == null || (dto.Device == null && !string.IsNullOrWhiteSpace(dto.User_Id)))
                {
                    return BadRequest("Invalid request data.");
                }

                if (!Request.Headers.TryGetValue("X-API-KEY", out var rawKey)) //get api key hash
                {
                    return Unauthorized("API Key is missing.");
                }

                //  #region API Key Validation
                var isValid = await _apiKeyRepository.TrackUsageAsync(rawKey);
                if (!isValid)
                {
                    return Unauthorized("Invalid API Key");
                }

                var apiKeyInfo = await _apiKeyRepository.GetApiByApiKeyIdAsync(rawKey);
                if (apiKeyInfo == null)
                {
                    return Unauthorized("API Key information not found");
                }

                //  #region Session Handling
                Request.Headers.TryGetValue("X-SESSION-ID", out var existingSessionId);
                Sessions existingSession = null;
                if (!string.IsNullOrWhiteSpace(existingSessionId))
                {
                    existingSession = await _sessionRepository.GetSessionByIdAsync(existingSessionId, apiKeyInfo.Id);
                }

                //  #region User Management
                if (!string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    var user = new Users
                    {
                        Tenant_Id = apiKeyInfo.Id,
                        User_Id = dto.User_Id,
                        Name = dto.Name,
                        Email = dto.Email,
                        Last_login = DateTime.UtcNow
                    };

                    var userinfo = await _userInfoRepository.getUserById(dto.User_Id, apiKeyInfo.Id);
                    if (userinfo == null)
                    {
                        user.Created_at = DateTime.UtcNow;
                        await _userInfoRepository.CreateUserAsync(user);
                    }
                    else
                    {
                        await _userInfoRepository.UpdateUserAsync(user);
                    }
                }

                //  #region Geolocation
                var location = await _geolocationService.GetGeolocationAsync(dto.Ip_Address);
                if (location == null)
                {
                    return BadRequest("Unable to retrieve geolocation data.");
                }

                //  #region Session Processing
                // Case 1: Anonymous session to logged-in user
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
                    await PublishSessionRiskCheck(existingSession.Id, apiKeyInfo, dto, location);
                    return Ok(new { SessionsId = existingSession.Id });
                }

                // Case 2: Anonymous session revisit
                if (existingSession != null && string.IsNullOrEmpty(existingSession.User_Id) && string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    return Ok(new { SessionsId = existingSession.Id });
                }

                // Case 3: Returning user session
                if (existingSession != null && !string.IsNullOrEmpty(existingSession.User_Id) && existingSession.User_Id == dto.User_Id)
                {
                    return Ok(new { SessionsId = existingSession.Id });
                }

                // Case 4: Create new session
                var session = CreateNewSession(apiKeyInfo.Id, dto, location);
                var sessionCreated = await _sessionRepository.CreateSessionAsync(session);

                if (!string.IsNullOrWhiteSpace(dto.User_Id))
                {
                    await PublishSessionRiskCheck(sessionCreated.Id, apiKeyInfo, dto, location);
                }

                return Ok(new { SessionsId = sessionCreated.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateSession for IP: {IpAddress}", dto?.Ip_Address);
                return StatusCode(500, "Internal server error.");
            }
        }

       // #region Helper Methods
        private async Task PublishSessionRiskCheck(string sessionId, Tenants apiKeyInfo, SessionRequestDto dto, GeoLocationDto location)
        {
            var message = new SessionRiskCheckMessage
            {
                SessionId = sessionId,
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
                    Browser = GetBrowserInfo(dto.Device?.UserAgent),
                    Fingerprint = dto.Device?.Fingerprint,
                    Device_Type = GetDeviceInfo(dto.Device?.UserAgent),
                    OS = GetOSInfo(dto.Device?.UserAgent),
                    Language = dto.Device?.Language,
                    Screen_Resolution = dto.Device?.Screen_Resolution
                }
            };
            _publisher.PublishSessionRiskCheck(message);
        }

        private Sessions CreateNewSession(string tenantId, SessionRequestDto dto, GeoLocationDto location)
        {
            return new Sessions
            {
                Tenant_Id = tenantId,
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
                    Browser = GetBrowserInfo(dto.Device?.UserAgent),
                    Fingerprint = dto.Device?.Fingerprint,
                    Device_Type = GetDeviceInfo(dto.Device?.UserAgent),
                    OS = GetOSInfo(dto.Device?.UserAgent),
                    Language = dto.Device?.Language,
                    Screen_Resolution = dto.Device?.Screen_Resolution
                }

            };
        }

        private static string GetOSInfo(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Unknown";
            string os = GetOperatingSystem(userAgent);
            string version = GetOSVersion(userAgent);
            return string.IsNullOrEmpty(version) ? os : $"{os} {version}";
        }

        private static string GetBrowserInfo(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Unknown";
            string browser = GetBrowserName(userAgent);
            string version = GetBrowserVersion(userAgent);
            return string.IsNullOrEmpty(version) ? browser : $"{browser} {version}";
        }

        private static string GetDeviceInfo(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Unknown";
            return GetDeviceType(userAgent);
        }

        private static string GetOperatingSystem(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Unknown";

            if (userAgent.Contains("Windows Phone", StringComparison.OrdinalIgnoreCase)) return "Windows Phone";
            if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
            if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
            if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
                userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
                userAgent.Contains("iPod", StringComparison.OrdinalIgnoreCase)) return "iOS";
            if (userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ||
                userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase)) return "MacOS";
            if (userAgent.Contains("CrOS", StringComparison.OrdinalIgnoreCase)) return "ChromeOS";
            if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux";

            return "Unknown";
        }

        private static string GetOSVersion(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return null;

            var iosMatch = Regex.Match(userAgent, @"OS (\d+[_\.\d]*)");
            if (iosMatch.Success) return iosMatch.Groups[1].Value.Replace("_", ".");

            var androidMatch = Regex.Match(userAgent, @"Android (\d+[\.\d]*)");
            if (androidMatch.Success) return androidMatch.Groups[1].Value;

            var macMatch = Regex.Match(userAgent, @"Mac OS X (\d+[_\.\d]*)");
            if (macMatch.Success) return macMatch.Groups[1].Value.Replace("_", ".");

            var winMatch = Regex.Match(userAgent, @"Windows NT (\d+[\.\d]*)");
            if (winMatch.Success) return winMatch.Groups[1].Value;

            return null;
        }

        private static string GetBrowserName(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Unknown";

            if (userAgent.Contains("Edg", StringComparison.OrdinalIgnoreCase)) return "Edge";
            if (userAgent.Contains("OPR", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Opera", StringComparison.OrdinalIgnoreCase)) return "Opera";
            if (userAgent.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase)) return "Vivaldi";
            if (userAgent.Contains("SamsungBrowser", StringComparison.OrdinalIgnoreCase)) return "Samsung Browser";
            if (userAgent.Contains("FxiOS", StringComparison.OrdinalIgnoreCase)) return "Firefox (iOS)";
            if (userAgent.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
            if (userAgent.Contains("CriOS", StringComparison.OrdinalIgnoreCase)) return "Chrome (iOS)";
            if (userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("CriOS")) return "Chrome";
            if (userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("Chrome") && !userAgent.Contains("CriOS")) return "Safari";
            if (userAgent.Contains("MSIE", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Trident", StringComparison.OrdinalIgnoreCase)) return "Internet Explorer";
            if (userAgent.Contains("MicroMessenger", StringComparison.OrdinalIgnoreCase)) return "WeChat Browser";
            if (userAgent.Contains("QQ", StringComparison.OrdinalIgnoreCase)) return "QQ Browser";

            return "Unknown";
        }

        private static string GetBrowserVersion(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return null;

            var criosMatch = Regex.Match(userAgent, @"CriOS/([\d\.]+)");
            if (criosMatch.Success) return criosMatch.Groups[1].Value;

            var fxiOSMatch = Regex.Match(userAgent, @"FxiOS/([\d\.]+)");
            if (fxiOSMatch.Success) return fxiOSMatch.Groups[1].Value;

            var chromeMatch = Regex.Match(userAgent, @"Chrome/([\d\.]+)");
            if (chromeMatch.Success) return chromeMatch.Groups[1].Value;

            var safariMatch = Regex.Match(userAgent, @"Version/([\d\.]+).*Safari");
            if (safariMatch.Success) return safariMatch.Groups[1].Value;

            var edgeMatch = Regex.Match(userAgent, @"Edg/([\d\.]+)");
            if (edgeMatch.Success) return edgeMatch.Groups[1].Value;

            return null;
        }

        private static string GetDeviceType(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Unknown";

            userAgent = userAgent.ToLowerInvariant();

            if (userAgent.Contains("ipad") || userAgent.Contains("tablet") ||
                (userAgent.Contains("android") && !userAgent.Contains("mobile")))
                return "Tablet";

            if (userAgent.Contains("mobi") || userAgent.Contains("iphone") || userAgent.Contains("ipod") ||
                (userAgent.Contains("android") && userAgent.Contains("mobile")))
                return "Mobile";

            return "Desktop";
        }

















        ///// <summary>
        ///// Creates or updates a user session
        ///// </summary>
        ///// <param name="dto">Session request data</param>
        ///// <returns>Action result with session ID</returns>

        //[HttpPost("create")]
        //public async Task<IActionResult> CreateSession([FromBody] SessionRequestDto dto)
        //{
        //    try
        //    {
        //        //string apiKey = Request.Headers["X-API-KEY"].FirstOrDefault() ?? Request.Query["apiKey"];
        //        //string sessionId = Request.Headers["X-SESSION-ID"].FirstOrDefault() ?? Request.Query["sessionId"];

        //        if (!Request.Headers.TryGetValue("X-API-KEY", out var rawKey))  //get api key hash
        //        {
        //            return Unauthorized("API Key is missing."); 
        //        }
        //        var isValid = await _apiKeyRepository.TrackUsageAsync(rawKey);   // track apk key using limit and validation in DB
        //        if (!isValid)
        //            return Unauthorized("Invalid API Key");

        //        var apiKeyInfo = await _apiKeyRepository.GetApiByApiKeyIdAsync(rawKey); // check info API key in DB

        //        Request.Headers.TryGetValue("X-SESSION-ID", out var existingSessionId); // get session id
        //        Sessions existingSession = null;
        //        if (!string.IsNullOrWhiteSpace(existingSessionId))
        //        {
        //            existingSession = await _sessionRepository.GetSessionByIdAsync(existingSessionId, apiKeyInfo.Id); // get session if it already exits
        //        }

        //        // create user or update user
        //        if (!string.IsNullOrWhiteSpace(dto.User_Id))
        //        {
        //            // dto user id not null
        //            var userinfo = await _userInfoRepository.getUserById(dto.User_Id, apiKeyInfo.Id);  // get user if it exits in DB
        //            var user = new Users
        //            {
        //                Tenant_Id = apiKeyInfo.Id,
        //                User_Id = dto.User_Id,
        //                Name = dto.Name,
        //                Email = dto.Email,
        //                Last_login = DateTime.UtcNow
        //            };

        //            if (userinfo == null) //if user is null than create user
        //            {
        //                user.Created_at = DateTime.UtcNow;
        //                await _userInfoRepository.CreateUserAsync(user);
        //            }
        //            else
        //            {
        //                await _userInfoRepository.UpdateUserAsync(user); // if user is exits than update login time
        //            }
        //        }

        //        var location = await _geolocationService.GetGeolocationAsync(dto.Ip_Address); // get location using ip address
        //        if (location == null)
        //        {
        //            return BadRequest("Unable to retrieve geolocation data.");
        //        }


        //        // Condition 1: Anonymous session gets associated with a logged-in user
        //        // If the session exists AND the session's User_Id is null (anonymous)
        //        // AND the request contains a valid User_Id,
        //        // then update the session to link it to the logged-in user.
        //        // update session for anonymous to loin user
        //        if (existingSession != null && string.IsNullOrEmpty(existingSession.User_Id) && !string.IsNullOrWhiteSpace(dto.User_Id))
        //        {
        //            var update = new Sessions
        //            {
        //                Tenant_Id = existingSession.Tenant_Id,
        //                Id = existingSession.Id,
        //                User_Id = dto.User_Id,
        //                isActive = true,
        //                Logout_Time = null,
        //            };
        //            await _sessionRepository.UpdateSessionAsync(update);
        //            // RabbitMQ 
        //            _publisher.PublishSessionRiskCheck(new SessionRiskCheckMessage
        //            {
        //                SessionId = existingSession.Id,
        //                TenantId = apiKeyInfo.Id,
        //                UserId = dto.User_Id,
        //                Email = dto.Email,
        //                Ip_Address = dto.Ip_Address,
        //                Cliend_Domaim = apiKeyInfo.Domain,
        //                Login_Time = DateTime.UtcNow,
        //                Geo_Location = new Models.DTOs.Location
        //                {
        //                    Country = location.Country,
        //                    City = location.City,
        //                    Region = location.Region,
        //                    Postal = location.Postal,
        //                    Latitude_Longitude = location.Loc,
        //                    Isp = location.Org,
        //                    TimeZone = location.TimeZone
        //                },
        //                Device = new Models.DTOs.DeviceInfo
        //                {
        //                    Browser = GetOperatingSystem(dto.Device.userAgent) + " " + GetBrowserVersion(dto.Device.userAgent),
        //                    Fingerprint = dto.Device.Fingerprint,
        //                    Device_Type = GetDeviceType(dto.Device.userAgent) + " " + GetBrowserName(dto.Device.userAgent),
        //                    OS = GetOperatingSystem(dto.Device.userAgent) + " " + GetOSVersion(dto.Device.userAgent),
        //                    Language = dto.Device.Language,
        //                    Screen_Resolution = dto.Device.Screen_Resolution
        //                }
        //            });
        //            return Ok(new { SessionsId = existingSession.Id });
        //        }

        //        // Condition 2: Anonymous session revisited without login
        //        // If the session exists AND the session's User_Id is null (anonymous)
        //        // AND the request also has no User_Id (still anonymous),
        //        // then just return the existing session ID without updating anything.
        //        else if (existingSession != null && string.IsNullOrEmpty(existingSession.User_Id) && string.IsNullOrWhiteSpace(dto.User_Id))
        //        { 
        //            return Ok(new { SessionsId = existingSession.Id }); // Condition 2: Anonymous → Return visit (no change)
        //        }

        //        // Condition 3: Returning user session
        //        // If the session exists AND it already has a User_Id
        //        // AND the incoming request also has the same User_Id,
        //        // then the user is returning, so just return the existing session ID.
        //        else if (existingSession != null && !string.IsNullOrEmpty(existingSession.User_Id) && !string.IsNullOrWhiteSpace(dto.User_Id) && existingSession.User_Id == dto.User_Id)
        //        {
        //            return Ok(new { SessionsId = existingSession.Id }); // User already exists → return
        //        }
        //        //create session
        //        else
        //        {
        //            var session = new Sessions
        //            {
        //                Tenant_Id = apiKeyInfo.Id,
        //                User_Id = dto?.User_Id,
        //                Ip_Address = dto.Ip_Address,
        //                isActive = true,
        //                Login_Time = DateTime.UtcNow,
        //                ReferrerUrl = dto.ReferrerUrl,

        //                Geo_Location = new Entities.Location
        //                {
        //                    Country = location.Country,
        //                    City = location.City,
        //                    Region = location.Region,
        //                    Postal = location.Postal,
        //                    Latitude_Longitude = location.Loc,
        //                    Isp = location.Org,
        //                    TimeZone = location.TimeZone
        //                },
        //                Device = new Entities.DeviceInfo
        //                {
        //                    Browser = GetOperatingSystem(dto.Device.userAgent)+ " " + GetBrowserVersion(dto.Device.userAgent),
        //                    Fingerprint = dto.Device.Fingerprint,
        //                    Device_Type = GetDeviceType(dto.Device.userAgent)+ " " + GetBrowserName(dto.Device.userAgent),
        //                    OS = GetOperatingSystem(dto.Device.userAgent) + " " + GetOSVersion(dto.Device.userAgent),
        //                    Language = dto.Device.Language,
        //                    Screen_Resolution = dto.Device.Screen_Resolution
        //                }
        //            };

        //            var sessionCreated = await _sessionRepository.CreateSessionAsync(session);

        //            if (!string.IsNullOrWhiteSpace(dto.User_Id))
        //            {
        //                _publisher.PublishSessionRiskCheck(new SessionRiskCheckMessage
        //                {
        //                    SessionId = sessionCreated.Id,
        //                    TenantId = apiKeyInfo.Id,
        //                    UserId = dto.User_Id,
        //                    Email = dto.Email,
        //                    Ip_Address = dto.Ip_Address,
        //                    Cliend_Domaim = apiKeyInfo.Domain,
        //                    Login_Time = DateTime.UtcNow,
        //                    Geo_Location = new Models.DTOs.Location
        //                    {
        //                        Country = location.Country,
        //                        City = location.City,
        //                        Region = location.Region,
        //                        Postal = location.Postal,
        //                        Latitude_Longitude = location.Loc,
        //                        Isp = location.Org,
        //                        TimeZone = location.TimeZone
        //                    },
        //                    Device = new Models.DTOs.DeviceInfo
        //                    {
        //                        Browser = GetOperatingSystem(dto.Device.userAgent) + " " + GetBrowserVersion(dto.Device.userAgent),
        //                        Fingerprint = dto.Device.Fingerprint,
        //                        Device_Type = GetDeviceType(dto.Device.userAgent) + " " + GetBrowserName(dto.Device.userAgent),
        //                        OS = GetOperatingSystem(dto.Device.userAgent) + " " + GetOSVersion(dto.Device.userAgent),
        //                        Language = dto.Device.Language,
        //                        Screen_Resolution = dto.Device.Screen_Resolution
        //                    }
        //                });
        //            }
             
        //            return Ok(new { SessionsId = sessionCreated.Id }); //create anonymous user

        //        } 
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error in CreateSession");
        //        return StatusCode(500, "Something went wrong.");
        //    }

        //}






        //private static string GetOperatingSystem(string userAgent)
        //{
        //    if (string.IsNullOrEmpty(userAgent)) return "Unknown";

        //    if (userAgent.Contains("Windows Phone", StringComparison.OrdinalIgnoreCase)) return "Windows Phone";
        //    if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
        //    if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
        //    if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
        //        userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
        //        userAgent.Contains("iPod", StringComparison.OrdinalIgnoreCase)) return "iOS";
        //    if (userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ||
        //        userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase)) return "MacOS";
        //    if (userAgent.Contains("CrOS", StringComparison.OrdinalIgnoreCase)) return "ChromeOS";
        //    if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux";

        //    return "Unknown";
        //}

        //private static string GetOSVersion(string userAgent)
        //{
        //    if (string.IsNullOrEmpty(userAgent)) return null;

        //    // iOS
        //    var iosMatch = Regex.Match(userAgent, @"OS (\d+[_\.\d]*)");
        //    if (iosMatch.Success) return iosMatch.Groups[1].Value.Replace("_", ".");

        //    // Android
        //    var androidMatch = Regex.Match(userAgent, @"Android (\d+[\.\d]*)");
        //    if (androidMatch.Success) return androidMatch.Groups[1].Value;

        //    // MacOS
        //    var macMatch = Regex.Match(userAgent, @"Mac OS X (\d+[_\.\d]*)");
        //    if (macMatch.Success) return macMatch.Groups[1].Value.Replace("_", ".");

        //    // Windows
        //    var winMatch = Regex.Match(userAgent, @"Windows NT (\d+[\.\d]*)");
        //    if (winMatch.Success) return winMatch.Groups[1].Value;

        //    return null;
        //}

        //private static string GetBrowserName(string userAgent)
        //{
        //    if (string.IsNullOrEmpty(userAgent)) return "Unknown";

        //    if (userAgent.Contains("Edg", StringComparison.OrdinalIgnoreCase)) return "Edge";
        //    if (userAgent.Contains("OPR", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Opera", StringComparison.OrdinalIgnoreCase)) return "Opera";
        //    if (userAgent.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase)) return "Vivaldi";
        //    if (userAgent.Contains("SamsungBrowser", StringComparison.OrdinalIgnoreCase)) return "Samsung Browser";
        //    if (userAgent.Contains("FxiOS", StringComparison.OrdinalIgnoreCase)) return "Firefox (iOS)";
        //    if (userAgent.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        //    if (userAgent.Contains("CriOS", StringComparison.OrdinalIgnoreCase)) return "Chrome (iOS)";
        //    if (userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("CriOS")) return "Chrome";
        //    if (userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("Chrome") && !userAgent.Contains("CriOS")) return "Safari";
        //    if (userAgent.Contains("MSIE", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Trident", StringComparison.OrdinalIgnoreCase)) return "Internet Explorer";
        //    if (userAgent.Contains("MicroMessenger", StringComparison.OrdinalIgnoreCase)) return "WeChat Browser";
        //    if (userAgent.Contains("QQ", StringComparison.OrdinalIgnoreCase)) return "QQ Browser";

        //    return "Unknown";
        //}

        //private static string GetBrowserVersion(string userAgent)
        //{
        //    if (string.IsNullOrEmpty(userAgent)) return null;

        //    var criosMatch = Regex.Match(userAgent, @"CriOS/([\d\.]+)");
        //    if (criosMatch.Success) return criosMatch.Groups[1].Value;

        //    var fxiOSMatch = Regex.Match(userAgent, @"FxiOS/([\d\.]+)");
        //    if (fxiOSMatch.Success) return fxiOSMatch.Groups[1].Value;

        //    var chromeMatch = Regex.Match(userAgent, @"Chrome/([\d\.]+)");
        //    if (chromeMatch.Success) return chromeMatch.Groups[1].Value;

        //    var safariMatch = Regex.Match(userAgent, @"Version/([\d\.]+).*Safari");
        //    if (safariMatch.Success) return safariMatch.Groups[1].Value;

        //    var edgeMatch = Regex.Match(userAgent, @"Edg/([\d\.]+)");
        //    if (edgeMatch.Success) return edgeMatch.Groups[1].Value;

        //    return null;
        //}

        //private static string GetDeviceType(string userAgent)
        //{
        //    if (string.IsNullOrEmpty(userAgent)) return "Unknown";

        //    userAgent = userAgent.ToLowerInvariant();

        //    // Tablet detection
        //    if (userAgent.Contains("ipad") || userAgent.Contains("tablet") ||
        //        (userAgent.Contains("android") && !userAgent.Contains("mobile")))
        //        return "Tablet";

        //    // Mobile detection
        //    if (userAgent.Contains("mobi") || userAgent.Contains("iphone") || userAgent.Contains("ipod") ||
        //        (userAgent.Contains("android") && userAgent.Contains("mobile")))
        //        return "Mobile";

        //    // Default: Desktop
        //    return "Desktop";
        //}

        // Optional: helper to get short info
        //public string GetShortInfo()
        //{
        //    string os = !string.IsNullOrEmpty(OSVersion) ? $"{OperatingSystem} {OSVersion}" : OperatingSystem;
        //    string browser = !string.IsNullOrEmpty(BrowserVersion) ? $"{BrowserName} {BrowserVersion}" : BrowserName;
        //    return $"OS: {os}\nBrowser: {browser}\nDevice Type: {DeviceType}";
        //}















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
