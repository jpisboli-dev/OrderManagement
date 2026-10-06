using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderManagement.Application;
using System.Text.Json;
namespace OrderManagement.Infrastructure;

public interface IOrderNotifier { Task Notify(StatusEvent status, Guid eventId, CancellationToken ct); }
public sealed class BackgroundJobs(AppDbContext db, IOrderNotifier notifier, ILogger<BackgroundJobs> logger)
{
    // SQL Server's distributed lock serializes dispatchers across API instances.
    // Delivery is at least once: clients deduplicate eventId on reconnect/retry.
    [DisableConcurrentExecution(300)]
    [AutomaticRetry(Attempts = 5)]
    public async Task Dispatch(CancellationToken ct)
    {
        var messages = await db.Outbox.Where(m => m.ProcessedAt == null).OrderBy(m => m.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var message in messages)
        {
            switch (message.Kind)
            {
                case "Confirmation":
                    var orderId = JsonSerializer.Deserialize<Guid>(message.Payload);
                    logger.LogInformation("E-mail SIMULADO: confirmação do pedido {OrderId}; evento {EventId}", orderId, message.Id);
                    break;
                case "Status":
                    await notifier.Notify(JsonSerializer.Deserialize<StatusEvent>(message.Payload)!, message.Id, ct);
                    break;
                case "Report":
                    var id = JsonSerializer.Deserialize<Guid>(message.Payload);
                    var report = await db.Reports.SingleAsync(r => r.Id == id, ct);
                    var orders = await db.Orders.Where(o => o.CreatedAt <= report.RequestedAt && o.Status != Domain.OrderStatus.Criado).ToListAsync(ct);
                    report.PaidOrders = orders.Count; report.Revenue = orders.Sum(o => o.Total); report.CompletedAt = DateTime.UtcNow;
                    break;
                default: throw new InvalidOperationException("Tipo de evento desconhecido.");
            }
            message.ProcessedAt = DateTime.UtcNow;
            await db.Save(ct);
        }
    }
}
