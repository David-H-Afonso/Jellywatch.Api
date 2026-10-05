using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Jellywatch.Api.Infrastructure.BackgroundJobs;

public sealed class PushNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WebPushSettings> options,
    ILogger<PushNotificationWorker> logger) : BackgroundService
{
    private readonly WebPushSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.HasVapidConfiguration)
        {
            logger.LogInformation("Jellywatch push notification worker is disabled or missing VAPID configuration.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(_settings.WorkerIntervalSeconds, 15, 3600)));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
                var sent = await service.DispatchDueAsync(stoppingToken);
                if (sent > 0) logger.LogInformation("Delivered {Count} Jellywatch push notifications.", sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Jellywatch push notification dispatch failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
