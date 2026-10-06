using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application;
using OrderManagement.Domain;
using OrderManagement.Infrastructure;
using Xunit;
namespace OrderManagement.Tests;
public class ApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task Login(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResult>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }
    [Fact] public async Task Complete_flow_persists_events_and_generates_report()
    {
        using var factory = new ApiFactory(); await factory.Initialize();
        using var admin = factory.CreateClient(); using var customer = factory.CreateClient();
        await Login(admin, "admin@example.com", "AdminPassword123!");
        var registered = await customer.PostAsJsonAsync("/api/auth/register", new { name = "Ana", email = "ana@example.com", password = "CustomerPassword123!" });
        registered.EnsureSuccessStatusCode();
        Assert.DoesNotContain("passwordHash", await registered.Content.ReadAsStringAsync());
        await Login(customer, "ana@example.com", "CustomerPassword123!");
        var productResponse = await admin.PostAsJsonAsync("/api/products", new { name = "Teclado", price = 99.90m });
        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        var product = await productResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orderResponse = await customer.PostAsJsonAsync("/api/orders", new { items = new[] { new { productId = product.GetProperty("id").GetGuid(), quantity = 2 } } });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = (await orderResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PatchAsJsonAsync($"/api/orders/{order}/status", new { status = "Pago" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/orders/{order}/status", new { status = "Enviado" })).StatusCode);
        foreach (var status in new[] { "Pago", "EmPreparo", "Enviado", "Entregue" })
            Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/orders/{order}/status", new { status })).StatusCode);
        var requested = await admin.PostAsync("/api/reports/sales", null);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var reportId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(6, await db.Outbox.CountAsync());
            await scope.ServiceProvider.GetRequiredService<BackgroundJobs>().Dispatch(default);
            await scope.ServiceProvider.GetRequiredService<BackgroundJobs>().Dispatch(default);
            Assert.Equal(0, await db.Outbox.CountAsync(m => m.ProcessedAt == null));
        }
        Assert.Equal(4, factory.Notifier.Events.Count);
        var report = (await admin.GetFromJsonAsync<SalesReport>($"/api/reports/sales/{reportId}", Json))!;
        Assert.Equal(199.80m, report.Revenue); Assert.Equal(1, report.PaidOrders); Assert.NotNull(report.CompletedAt);
    }
    [Fact] public async Task Authorization_and_validation_are_enforced()
    {
        using var factory = new ApiFactory(); await factory.Initialize(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.com", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { name = "Ana", email = "bad-email", password = "CustomerPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { name = "Ana", email = "ana@example.com", password = "short" })).StatusCode);
        var input = new { name = "Ana", email = "ana@example.com", password = "CustomerPassword123!", role = "Admin" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", input)).StatusCode);
        await Login(client, input.email, input.password);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/products", new { name = "Mouse", price = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/reports/sales", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", new { items = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/products?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/orders/{Guid.NewGuid()}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "forged-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
    }
    [Fact] public async Task Customer_cannot_read_another_customers_order()
    {
        using var factory = new ApiFactory(); await factory.Initialize(); using var client = factory.CreateClient();
        Guid orderId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.Customers.SingleAsync(); var product = new Product("Mouse", 50); db.Products.Add(product);
            var order = new Order(admin.Id, [new(product, 1)]); db.Orders.Add(order); await db.SaveChangesAsync(); orderId = order.Id;
        }
        await client.PostAsJsonAsync("/api/auth/register", new { name = "Ana", email = "ana@example.com", password = "CustomerPassword123!" });
        await Login(client, "ana@example.com", "CustomerPassword123!");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/orders/{orderId}")).StatusCode);
        Assert.Equal("[]", await client.GetStringAsync("/api/orders"));
    }
    [Fact] public async Task Concurrent_status_change_is_rejected()
    {
        using var factory = new ApiFactory(); await factory.Initialize();
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var customer = await db.Customers.SingleAsync();
            var p = new Product("Mouse", 50); db.Products.Add(p); var o = new Order(customer.Id, [new(p, 1)]); db.Orders.Add(o); await db.SaveChangesAsync(); id = o.Id;
        }
        using var scope1 = factory.Services.CreateScope(); using var scope2 = factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>(); var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await db1.Orders.SingleAsync(o => o.Id == id); var second = await db2.Orders.SingleAsync(o => o.Id == id);
        first.Advance(OrderStatus.Pago); second.Advance(OrderStatus.Pago); await db1.Save(default);
        await Assert.ThrowsAsync<ConflictException>(() => db2.Save(default));
    }
    [Fact] public async Task Openapi_and_signalr_negotiate_require_authentication()
    {
        using var factory = new ApiFactory(); await factory.Initialize(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/hubs/orders/negotiate?negotiateVersion=1", null)).StatusCode);
        await Login(client, "admin@example.com", "AdminPassword123!");
        var openapi = await client.GetAsync("/openapi/v1.json");
        openapi.EnsureSuccessStatusCode();
        Assert.Contains("/api/orders/{id}/status", await openapi.Content.ReadAsStringAsync());
        var connection = await client.PostAsync("/hubs/orders/negotiate?negotiateVersion=1", null);
        connection.EnsureSuccessStatusCode();
        Assert.Contains("connectionToken", await connection.Content.ReadAsStringAsync());
    }
}
