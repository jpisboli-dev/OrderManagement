using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using OrderManagement.Application;
using OrderManagement.Domain;
namespace OrderManagement.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<SalesReport> Reports => Set<SalesReport>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>().Property(p => p.Name).HasMaxLength(160);
        b.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);
        b.Entity<Customer>().HasIndex(c => c.Email).IsUnique();
        b.Entity<Customer>().Property(c => c.Email).HasMaxLength(254);
        b.Entity<Customer>().Property(c => c.Name).HasMaxLength(160);
        b.Entity<Order>().Ignore(o => o.Total);
        b.Entity<Order>().Property(o => o.Version).IsConcurrencyToken();
        b.Entity<Order>().HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>().HasMany(o => o.Items).WithOne().HasForeignKey("OrderId");
        b.Entity<Order>().Navigation(o => o.Items).AutoInclude();
        b.Entity<OrderItem>().Property(i => i.UnitPrice).HasPrecision(18, 2);
        b.Entity<OrderItem>().HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SalesReport>().Property(r => r.Revenue).HasPrecision(18, 2);
        b.Entity<OutboxMessage>().HasIndex(m => m.ProcessedAt);
    }
    public async Task Save(CancellationToken ct)
    {
        try { await SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Pedido alterado por outra operação. Recarregue e tente novamente."); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 }) { throw new ConflictException("Registro já existente."); }
    }
}
public sealed class Repository<T>(AppDbContext db) : IRepository<T> where T : Entity
{
    public Task<T?> Get(Guid id, CancellationToken ct) => db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct);
    public async Task<IReadOnlyList<T>> List(Expression<Func<T, bool>>? filter, int skip, int take, CancellationToken ct)
    {
        IQueryable<T> query = db.Set<T>();
        if (filter is not null) query = query.Where(filter);
        return await query.OrderBy(e => e.Id).Skip(skip).Take(take).ToListAsync(ct);
    }
    public void Add(T entity) => db.Set<T>().Add(entity);
}
