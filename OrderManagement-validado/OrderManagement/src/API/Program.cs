using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrderManagement.API;
using OrderManagement.Application;
using OrderManagement.Domain;
using OrderManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var testing = builder.Environment.IsEnvironment("Testing");
builder.Services.AddDbContext<AppDbContext>(o => { if (!testing) o.UseSqlServer(builder.Configuration.GetConnectionString("Database")); });
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUnitOfWork>(s => s.GetRequiredService<AppDbContext>());
builder.Services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<CreateOrder>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<IPasswords, Passwords>();
builder.Services.AddScoped<ITokens, Tokens>();
builder.Services.AddScoped<IOrderNotifier, OrderNotifier>();
builder.Services.AddScoped<BackgroundJobs>();
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    var key = builder.Configuration["Jwt:Key"]!;
    o.TokenValidationParameters = new() { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ClockSkew = TimeSpan.FromSeconds(15) };
    o.Events = new JwtBearerEvents { OnMessageReceived = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/hubs/orders") && ctx.Request.Query.TryGetValue("access_token", out var token)) ctx.Token = token;
        return Task.CompletedTask;
    }};
});
builder.Services.AddAuthorization(o => o.AddPolicy("AdminOnly", p => p.RequireRole("Admin")));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
if (!testing)
{
    builder.Services.AddHangfire(c => c.UseSqlServerStorage(builder.Configuration.GetConnectionString("Database"), new Hangfire.SqlServer.SqlServerStorageOptions { QueuePollInterval = TimeSpan.FromSeconds(1) }));
    builder.Services.AddHangfireServer();
    builder.Services.AddHostedService<OutboxPump>();
}
var app = builder.Build();
var jwtKey = app.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Configure Jwt__Key com pelo menos 32 bytes.");
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    try { await next(ctx); }
    catch (Exception e) when (e is RuleException or MissingException or UnauthorizedException or UnauthorizedAccessException or ConflictException)
    {
        var code = e switch { MissingException => 404, UnauthorizedException => 401, UnauthorizedAccessException => 403, ConflictException => 409, _ => 400 };
        await Results.Problem(statusCode: code, title: e.Message).ExecuteAsync(ctx);
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapOpenApi().RequireAuthorization("AdminOnly");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/auth/register", async (RegisterCustomer r, ISender m, CancellationToken ct) => Results.Ok(await m.Send(r, ct))).RequireRateLimiting("auth");
app.MapPost("/api/auth/login", async (Login r, ISender m, CancellationToken ct) => Results.Ok(await m.Send(r, ct))).RequireRateLimiting("auth");
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/products", async (int? page, ISender m, CancellationToken ct) => await m.Send(new ListProducts(page ?? 1), ct));
api.MapPost("/products", async (CreateProduct r, ISender m, CancellationToken ct) => { var p = await m.Send(r, ct); return Results.Created($"/api/products", p); }).RequireAuthorization(p => p.RequireRole("Admin"));
api.MapGet("/customers", async (int? page, ISender m, CancellationToken ct) => await m.Send(new ListCustomers(page ?? 1), ct)).RequireAuthorization(p => p.RequireRole("Admin"));
api.MapGet("/orders", async (int? page, ISender m, CancellationToken ct) => await m.Send(new ListOrders(page ?? 1), ct));
api.MapGet("/orders/{id:guid}", async (Guid id, ISender m, CancellationToken ct) => await m.Send(new GetOrder(id), ct));
api.MapPost("/orders", async (CreateOrder r, ISender m, CancellationToken ct) => { var o = await m.Send(r, ct); return Results.Created($"/api/orders/{o.Id}", o); });
api.MapPatch("/orders/{id:guid}/status", async (Guid id, StatusInput r, ISender m, CancellationToken ct) => await m.Send(new ChangeStatus(id, r.Status), ct)).RequireAuthorization(p => p.RequireRole("Admin"));
api.MapPost("/reports/sales", async (ISender m, CancellationToken ct) => { var id = await m.Send(new RequestReport(), ct); return Results.Accepted($"/api/reports/sales/{id}", new { id }); }).RequireAuthorization(p => p.RequireRole("Admin"));
api.MapGet("/reports/sales/{id:guid}", async (Guid id, ISender m, CancellationToken ct) => await m.Send(new GetReport(id), ct)).RequireAuthorization(p => p.RequireRole("Admin"));
app.MapHub<OrdersHub>("/hubs/orders", o => o.CloseOnAuthenticationExpiration = true);
if (!testing)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    var email = builder.Configuration["Bootstrap:AdminEmail"];
    var password = builder.Configuration["Bootstrap:AdminPassword"];
    if (!string.IsNullOrWhiteSpace(email) && !await db.Customers.AnyAsync(c => c.Role == "Admin"))
    {
        if (password is null || password.Length is < 12 or > 128) throw new InvalidOperationException("Configure Bootstrap__AdminPassword com 12 a 128 caracteres.");
        db.Customers.Add(new Customer("Administrador", email, scope.ServiceProvider.GetRequiredService<IPasswords>().Hash(password), "Admin"));
        await db.SaveChangesAsync();
    }
}
app.Run();
public record StatusInput(OrderStatus Status);
public partial class Program;
