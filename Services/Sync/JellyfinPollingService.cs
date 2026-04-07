using Microsoft.Extensions.Options;
using Jellywatch.Api.Configuration;

namespace Jellywatch.Api.Services.Sync;

public class JellyfinPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JellyfinSettings _settings;
    private readonly ILogger<JellyfinPollingService> _logger;

    public JellyfinPollingService(IServiceScopeFactory scopeFactory, IOptions<JellyfinSettings> settings, ILogger<JellyfinPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.PollingEnabled)
        {
            _logger.LogInformation("Jellyfin polling is disabled");
            return;
        }

        _logger.LogInformation("Jellyfin polling service started with interval: {Minutes} minutes", _settings.PollingIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_settings.PollingIntervalMinutes), stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<ISyncOrchestrationService>();
                await syncService.RunFullSyncAsync();

                _logger.LogInformation("Polling sync completed at {Time}", DateTime.UtcNow);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during polling sync");
            }
        }
    }
}
