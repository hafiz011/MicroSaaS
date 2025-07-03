using Microservice.Session.Models;
using Microservice.Session.Entities;
using Microservice.Session.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Microservice.Session.Infrastructure.AlertNotifier
{
    public class SuspiciousSessionDetection : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SuspiciousSessionDetection> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);
        public SuspiciousSessionDetection(IServiceProvider serviceProvider, ILogger<SuspiciousSessionDetection> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SuspiciousSessionDetectionService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DetectSuspiciousSessionsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while detecting suspicious sessions.");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("SuspiciousSessionDetectionService stopped.");
        }

        private async Task DetectSuspiciousSessionsAsync(CancellationToken token)
        {
            using var scope = _serviceProvider.CreateScope();
            var sessionRepository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
            var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
            var apiKeyRepository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();

            var newSessions = await sessionRepository.GetRecentUndetectedSessionsAsync();

            foreach (var session in newSessions)
            {
                try
                {
                    if (token.IsCancellationRequested) break;

                    var recentSessions = await sessionRepository.GetRecentSessionsForUserAsync(session.Tenant_Id, session.User_Id, 5);

                    session.IsAnalyzed = true;

                    if (recentSessions == null || !recentSessions.Any())
                    {
                        session.isSuspicious = false;
                        session.Suspicious_Flags = null;
                        session.SuspiciousDetectedAt = null;
                        await sessionRepository.UpdateSuspiciousSessionAsync(session.Id, session);
                        continue;
                    }

                    //var baseline = recentSessions.First(); // Compare with the most recent session

                    // Exclude the session itself from comparison
                    var baseline = recentSessions
                        .Where(s => s.Id != session.Id)
                        .OrderByDescending(s => s.Login_Time)
                        .FirstOrDefault();

                    if (baseline == null)
                    {
                        session.isSuspicious = false;
                        session.Suspicious_Flags = null;
                        session.SuspiciousDetectedAt = null;
                        await sessionRepository.UpdateSuspiciousSessionAsync(session.Id, session);
                        continue;
                    }

                    var flags = new List<string>();

                    if (baseline.Ip_Address != session.Ip_Address)
                    {
                        flags.Add("IP address mismatch");
                    }
                    if (baseline.Geo_Location?.Country != session.Geo_Location?.Country || baseline.Geo_Location?.City != session.Geo_Location?.City)
                    {
                        flags.Add("Location mismatch");
                    }
                    if (baseline.Device?.Fingerprint != session.Device?.Fingerprint)
                    {
                        flags.Add("Device fingerprint mismatch");
                    }
                    if (flags.Any())
                    {
                        session.isSuspicious = true;
                        //session.Suspicious_Flags = string.Join(", ", flags);
                        session.Suspicious_Flags = string.Join(", ", flags.Select(f => $"- {f}"));
                        session.SuspiciousDetectedAt = DateTime.UtcNow;

                        _logger.LogWarning($"Suspicious session for user {session.User_Id}: {session.Suspicious_Flags}");

                        var apiKey = await apiKeyRepository.GetApiKeyIdAsync(session.Tenant_Id);
                        var to = apiKey.Org_Email;
                        var subject = "Suspicious Login Detected";
                        var body = $"""
                                A suspicious login was detected for your account:

                                - IP Address: {session.Ip_Address}
                                - Location: {session.Geo_Location?.Country}
                                - Device: {session.Device?.Fingerprint}
                                - Flags: {session.Suspicious_Flags}
                                - Time: {session.Login_Time}

                                If this was not you, please secure your account immediately.
                                """;

                        bool emailSent = await emailService.SendEmailAsync(to, subject, body);

                        if (!emailSent)
                        {
                            _logger.LogError($"Failed to send suspicious login alert email to {to} for user {session.User_Id}.");
                        }
                    }

                    else
                    {
                        session.isSuspicious = false;
                        session.Suspicious_Flags = null;
                        session.SuspiciousDetectedAt = null;
                    }

                    await sessionRepository.UpdateSuspiciousSessionAsync(session.Id, session);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing session ID {session.Id}");
                }

            }
        }
    }
}
