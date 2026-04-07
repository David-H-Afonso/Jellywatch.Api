using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Sync;

namespace Jellywatch.Api.Controllers;

[Route("api/integrations/jellyfin")]
[ApiController]
[AllowAnonymous]
public class WebhookController : ControllerBase
{
    private readonly JellywatchDbContext _context;
    private readonly ISyncOrchestrationService _syncService;
    private readonly JellyfinSettings _settings;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(JellywatchDbContext context, ISyncOrchestrationService syncService, IOptions<JellyfinSettings> settings, ILogger<WebhookController> logger)
    {
        _context = context;
        _syncService = syncService;
        _settings = settings.Value;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> ReceiveWebhook()
    {
        _logger.LogInformation("Webhook request received from {RemoteIp}", HttpContext.Connection.RemoteIpAddress);

        string rawPayload;
        using (var reader = new StreamReader(Request.Body))
        {
            rawPayload = await reader.ReadToEndAsync();
        }

        var log = new WebhookEventLog
        {
            RawPayload = rawPayload,
            ReceivedAt = DateTime.UtcNow,
        };

        // Validate webhook secret if configured
        if (!string.IsNullOrEmpty(_settings.WebhookSecret))
        {
            var secret = Request.Headers["X-Webhook-Secret"].FirstOrDefault();
            if (secret != _settings.WebhookSecret)
            {
                _logger.LogWarning("Webhook request rejected — invalid secret");
                log.EventType = "Rejected";
                log.Success = false;
                log.ErrorMessage = "Invalid webhook secret";
                log.ProcessedAt = DateTime.UtcNow;
                _context.WebhookEventLogs.Add(log);
                await _context.SaveChangesAsync();
                return Unauthorized();
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            var root = doc.RootElement;

            var eventType = root.TryGetProperty("NotificationType", out var notifType)
                ? notifType.GetString()
                : root.TryGetProperty("Event", out var evt) ? evt.GetString() : "Unknown";

            log.EventType = eventType ?? "Unknown";

            // Process relevant events
            if (eventType is "PlaybackStart" or "PlaybackStop" or "PlaybackProgress" or "UserDataSaved")
            {
                var userId = root.TryGetProperty("UserId", out var uid) ? uid.GetString() : null;
                var itemId = root.TryGetProperty("ItemId", out var iid) ? iid.GetString() : null;

                if (userId != null && itemId != null)
                {
                    // Find profile by Jellyfin user ID
                    var profile = _context.Profiles.FirstOrDefault(p => p.JellyfinUserId == userId);
                    if (profile != null)
                    {
                        var watchEventType = eventType switch
                        {
                            "PlaybackStart" => WatchEventType.Started,
                            "PlaybackStop" => WatchEventType.Stopped,
                            "PlaybackProgress" => WatchEventType.Progress,
                            "UserDataSaved" => WatchEventType.Finished,
                            _ => WatchEventType.Progress,
                        };

                        long positionTicks = root.TryGetProperty("PlaybackPositionTicks", out var posTicks)
                            ? posTicks.GetInt64()
                            : 0;

                        await _syncService.ProcessWatchEventAsync(
                            profile.Id, itemId, watchEventType, positionTicks, SyncSource.Webhook);
                    }
                }

                log.Success = true;
                log.ProcessedAt = DateTime.UtcNow;
            }
            else
            {
                log.Success = true;
                log.ProcessedAt = DateTime.UtcNow;
                _logger.LogDebug("Ignored webhook event type: {EventType}", eventType);
            }
        }
        catch (Exception ex)
        {
            log.Success = false;
            log.ErrorMessage = ex.Message;
            log.ProcessedAt = DateTime.UtcNow;
            _logger.LogError(ex, "Error processing webhook");
        }

        _context.WebhookEventLogs.Add(log);
        await _context.SaveChangesAsync();

        return Ok();
    }
}
