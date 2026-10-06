using Hangfire;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Infrastructure;
namespace OrderManagement.API;
// A short durable Hangfire job drains the transactional outbox every two seconds.
public sealed class OutboxPump(IServiceScopeFactory scopes, ILogger<OutboxPump> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<AppDbContext>().Outbox.AnyAsync(m => m.ProcessedAt == null, stoppingToken)) continue;
                scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>().Enqueue<BackgroundJobs>(j => j.Dispatch(CancellationToken.None));
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested) { logger.LogError(e, "Falha ao agendar outbox; nova tentativa em 2 segundos."); }
        }
    }
}
