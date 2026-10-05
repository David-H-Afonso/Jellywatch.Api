using Jellywatch.Api.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Jellywatch.Api.Controllers;

[Route("api/notifications")]
public sealed class NotificationsController(IPushNotificationService notifications) : BaseApiController
{
    [HttpGet("config")]
    public IActionResult GetConfiguration() => Ok(new { enabled = notifications.IsConfigured, publicKey = notifications.PublicKey });

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken) =>
        Ok(new { seasonUpdates = await notifications.GetSeasonNotificationsPreferenceAsync(CurrentUserId!.Value, cancellationToken) });

    [HttpPut("preferences")]
    public async Task<IActionResult> SetPreferences([FromBody] UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        var enabled = await notifications.SetSeasonNotificationsPreferenceAsync(CurrentUserId!.Value, request.SeasonUpdates, cancellationToken);
        return Ok(new { seasonUpdates = enabled });
    }

    [HttpPost("subscription")]
    public async Task<IActionResult> UpsertSubscription([FromBody] PushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (!notifications.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Push notifications are not enabled on this server." });
        if (string.IsNullOrWhiteSpace(request.Endpoint) || string.IsNullOrWhiteSpace(request.P256dh) || string.IsNullOrWhiteSpace(request.Auth))
            return BadRequest(new { message = "Endpoint, p256dh and auth are required." });

        try
        {
            await notifications.UpsertSubscriptionAsync(CurrentUserId!.Value, request, cancellationToken);
            return Ok(new { enabled = true });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("subscription/status")]
    public async Task<IActionResult> GetSubscriptionStatus([FromBody] DeletePushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint)) return BadRequest(new { message = "Endpoint is required." });
        var active = await notifications.IsSubscriptionActiveAsync(CurrentUserId!.Value, request.Endpoint, cancellationToken);
        return Ok(new { active });
    }

    [HttpDelete("subscription")]
    public async Task<IActionResult> DeactivateSubscription([FromBody] DeletePushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint)) return BadRequest(new { message = "Endpoint is required." });
        await notifications.DeactivateSubscriptionAsync(CurrentUserId!.Value, request.Endpoint, cancellationToken);
        return NoContent();
    }
}

public sealed class DeletePushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
}

public sealed class UpdateNotificationPreferencesRequest
{
    public bool SeasonUpdates { get; set; }
}
