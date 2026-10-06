using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderManagement.Application;
using OrderManagement.Domain;
using OrderManagement.Infrastructure;
namespace OrderManagement.Tests;
public sealed class RecordingNotifier : IOrderNotifier
{
    public List<StatusEvent> Events { get; } = [];
    public Task Notify(StatusEvent status, Guid eventId, CancellationToken ct) { Events.Add(status); return Task.CompletedTask; }
}
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public RecordingNotifier Notifier { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Jwt:Key"] = "test-only-key-for-integration-tests-123456789", ["Jwt:Issuer"] = "OrderManagement", ["Jwt:Audience"] = "OrderManagement.Clients" }));
        builder.ConfigureServices(s =>
        {
            s.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
            s.RemoveAll<IOrderNotifier>(); s.AddSingleton<IOrderNotifier>(Notifier);
        });
    }
    public async Task Initialize()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Customers.Add(new Customer("Admin", "admin@example.com", scope.ServiceProvider.GetRequiredService<IPasswords>().Hash("AdminPassword123!"), "Admin"));
        await db.SaveChangesAsync();
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) connection.Dispose(); }
}
