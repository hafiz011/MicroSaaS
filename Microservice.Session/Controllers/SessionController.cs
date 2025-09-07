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
            public string IpAddress { get; set; }
            public string ReferrerUrl { get; set; }
            public string Fingerprint { get; set; }
            public string UserAgent { get; set; }
            public string Language { get; set; }
            public string ScreenResolution { get; set; }
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
                if (dto == null)
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

                //  #region Geolocation
                var location = await _geolocationService.GetGeolocationAsync(dto.IpAddress);
                if (location == null)
                {
                    return BadRequest("Unable to retrieve geolocation data.");
                }

                // Create new session
                var session = CreateNewSession(apiKeyInfo.Id, dto, location);
                var sessionCreated = await _sessionRepository.CreateSessionAsync(session);

                //if (!string.IsNullOrWhiteSpace(dto.User_Id))
                //{
                //    await PublishSessionRiskCheck(sessionCreated.Id, apiKeyInfo, dto, location);
                //}

                return Ok(new { SessionsId = sessionCreated.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateSession for IP: {IpAddress}", dto?.IpAddress);
                return StatusCode(500, "Internal server error.");
            }
        }


        private Sessions CreateNewSession(string tenantId, SessionRequestDto dto, GeoLocationDto location)
        {
            return new Sessions
            {
                Tenant_Id = tenantId,
                Ip_Address = dto.IpAddress,
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
                    Browser = GetBrowserInfo(dto?.UserAgent),
                    Fingerprint = dto?.Fingerprint,
                    Device_Type = GetDeviceInfo(dto?.UserAgent),
                    OS = GetOSInfo(dto?.UserAgent),
                    Language = dto?.Language,
                    Screen_Resolution = dto?.ScreenResolution
                }
            };
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

        private static string GetOSInfo(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Unknown";
            string os = GetOperatingSystem(userAgent);
            string version = GetOSVersion(userAgent);
            return string.IsNullOrEmpty(version) ? os : $"{os} {version}";
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
            public string UserId { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string ActionType { get; set; }     // view_product, add_to_cart, checkout, payment, etc.
            public string ProductId { get; set; }
            public string CategoryId { get; set; }
            public int Quantity { get; set; }
            public double Price { get; set; }
            public string Url { get; set; }
            public string ReferrerUrl { get; set; }
            public Dictionary<string, string> Metadata { get; set; }
            public string RequestMethod { get; set; }
            public int ResponseCode { get; set; }
            public bool SuccessFlag { get; set; }
            public double ResponseTime { get; set; }
            public DateTime TimeStamp { get; set; }
        }


        [HttpPost("log-activity")]
        public async Task<IActionResult> Log([FromBody] ActivityLogDto dto)
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

                var sessiondata = await _sessionRepository.GetSessionByIdAsync(sessionId, apiKeyInfo.Id);
                if (sessiondata == null)
                    return BadRequest("Session ID does not match.");

                // --- User info handling ---
                if (string.IsNullOrWhiteSpace(sessiondata.User_Id) && !string.IsNullOrWhiteSpace(dto.UserId))
                {
                    var userinfo = await _userInfoRepository.getUserById(dto.UserId, apiKeyInfo.Id);

                    if (userinfo == null)
                    {
                        var newUser = new Users
                        {
                            Tenant_Id = apiKeyInfo.Id,
                            User_Id = dto.UserId,
                            Name = dto.Name,
                            Email = dto.Email,
                            Created_at = DateTime.UtcNow,
                            Last_login = DateTime.UtcNow
                        };
                        await _userInfoRepository.CreateUserAsync(newUser);
                    }
                    else
                    {
                        userinfo.Last_login = DateTime.UtcNow;
                        if (!string.IsNullOrWhiteSpace(dto.Name)) userinfo.Name = dto.Name;
                        if (!string.IsNullOrWhiteSpace(dto.Email)) userinfo.Email = dto.Email;

                        await _userInfoRepository.UpdateUserAsync(userinfo);
                    }

                    sessiondata.User_Id = dto.UserId;
                    sessiondata.Logout_Time = null;
                    sessiondata = await _sessionRepository.UpdateSessionAsync(sessiondata);
                }

                // --- Session activation check ---
                if (!sessiondata.isActive)
                {
                    sessiondata.isActive = true;
                    sessiondata.Logout_Time = null;
                    sessiondata = await _sessionRepository.UpdateSessionAsync(sessiondata);
                }

                // --- Activity Log ---
                var log = new ActivityLog
                {
                    Tenant_Id = apiKeyInfo.Id,
                    Session_Id = sessiondata.Id,
                    User_Id = sessiondata.User_Id,
                    Action_Type = dto.ActionType,
                    Product_Id = dto.ProductId,
                    Category_Id = dto.CategoryId,
                    Quantity = dto.Quantity,
                    Price = dto.Price,
                    Url = dto.Url,
                    ReferrerUrl = dto.ReferrerUrl,
                    Metadata = dto.Metadata,
                    Request_Method = dto.RequestMethod,
                    Response_Code = dto.ResponseCode,
                    Success_Flag = dto.SuccessFlag,
                    Response_Time = dto.ResponseTime,
                    Time_Stamp = DateTime.UtcNow
                };

                await _activityRepository.CreateLogActivityAsync(log);

                return Ok(new
                {
                    Message = "Activity logged successfully",
                    Session = sessiondata,
                    Activity = log
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Log activity");
                return StatusCode(500, "An unexpected error occurred while logging the activity.");
            }
        }




        //// #region Helper Methods
        //private async Task PublishSessionRiskCheck(string sessionId, Tenants apiKeyInfo, SessionRequestDto dto, GeoLocationDto location)
        //{
        //    var message = new SessionRiskCheckMessage
        //    {
        //        SessionId = sessionId,
        //        TenantId = apiKeyInfo.Id,
        //        UserId = dto.UserId,
        //        Email = dto.Email,
        //        Ip_Address = dto.IpAddress,
        //        Cliend_Domaim = apiKeyInfo.Domain,
        //        Login_Time = DateTime.UtcNow,
        //        Geo_Location = new Models.DTOs.Location
        //        {
        //            Country = location.Country,
        //            City = location.City,
        //            Region = location.Region,
        //            Postal = location.Postal,
        //            Latitude_Longitude = location.Loc,
        //            Isp = location.Org,
        //            TimeZone = location.TimeZone
        //        },
        //        Device = new Models.DTOs.DeviceInfo
        //        {
        //            Browser = GetBrowserInfo(dto.Device?.UserAgent),
        //            Fingerprint = dto.Device?.Fingerprint,
        //            Device_Type = GetDeviceInfo(dto.Device?.UserAgent),
        //            OS = GetOSInfo(dto.Device?.UserAgent),
        //            Language = dto.Device?.Language,
        //            Screen_Resolution = dto.Device?.Screen_Resolution
        //        }
        //    };
        //    _publisher.PublishSessionRiskCheck(message);
        //}



    }
}
