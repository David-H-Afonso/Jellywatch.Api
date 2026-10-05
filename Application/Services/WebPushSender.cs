using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Domain.Entities;
using Microsoft.Extensions.Options;
using WebPush;
using DomainPushSubscription = Jellywatch.Api.Domain.Entities.PushSubscription;

namespace Jellywatch.Api.Application.Services;

public sealed class WebPushSender(IOptions<WebPushSettings> options, ILogger<WebPushSender> logger) : IWebPushSender, IDisposable
{
    private readonly WebPushSettings _settings = options.Value;
    private readonly WebPushClient _client = new();

    public async Task<WebPushSendResult> SendAsync(DomainPushSubscription subscription, string payload, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled || !_settings.HasVapidConfiguration)
            return new WebPushSendResult(false, Error: "Web Push is not configured.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _client.SendNotificationAsync(
                new WebPush.PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                payload,
                new VapidDetails(_settings.Subject, _settings.PublicKey, _settings.PrivateKey));
            return new WebPushSendResult(true);
        }
        catch (WebPushException exception)
        {
            logger.LogWarning(exception, "Web Push delivery failed with status {StatusCode}.", (int)exception.StatusCode);
            return new WebPushSendResult(false, (int)exception.StatusCode, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new WebPushSendResult(false, Error: exception.Message);
        }
    }

    public void Dispose() => _client.Dispose();
}
