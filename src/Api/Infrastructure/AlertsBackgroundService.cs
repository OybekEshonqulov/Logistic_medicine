using Microsoft.Extensions.Options;
using PharmaTrack.Application.Features.Analytics;
using PharmaTrack.Application.Features.Notifications;

namespace PharmaTrack.Api.Infrastructure;

public sealed class AlertsBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<AlertOptions> options,
    ILogger<AlertsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var created = await scope.ServiceProvider.GetRequiredService<AlertService>().RunAsync(stoppingToken);
                logger.LogInformation("Alert run finished, {Created} notifications created", created);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Alert run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
