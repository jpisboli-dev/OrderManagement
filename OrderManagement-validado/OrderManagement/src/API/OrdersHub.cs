using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OrderManagement.Application;
using OrderManagement.Infrastructure;
namespace OrderManagement.API;
[Authorize]
public sealed class OrdersHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User!.IsInRole("Admin")) await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
        await base.OnConnectedAsync();
    }
}
public sealed class OrderNotifier(IHubContext<OrdersHub> hub) : IOrderNotifier
{
    public async Task Notify(StatusEvent status, Guid eventId, CancellationToken ct)
    {
        var payload = new { eventId, status.OrderId, status.Status, status.Version };
        await hub.Clients.User(status.CustomerId.ToString()).SendAsync("OrderStatusChanged", payload, ct);
        await hub.Clients.Group("admins").SendAsync("OrderStatusChanged", payload, ct);
    }
}
