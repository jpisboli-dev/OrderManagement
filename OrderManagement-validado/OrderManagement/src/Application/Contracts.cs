using System.Linq.Expressions;
using OrderManagement.Domain;
namespace OrderManagement.Application;

public interface IRepository<T> where T : Entity
{
    Task<T?> Get(Guid id, CancellationToken ct);
    Task<IReadOnlyList<T>> List(Expression<Func<T, bool>>? filter, int skip, int take, CancellationToken ct);
    void Add(T entity);
}
public interface IUnitOfWork { Task Save(CancellationToken ct); }
public interface ICurrentUser { Guid Id { get; } bool IsAdmin { get; } }
public interface IPasswords { string Hash(string password); bool Verify(string hash, string password); }
public interface ITokens { string Create(Customer customer); }
public class MissingException() : Exception("Recurso não encontrado.");
public class UnauthorizedException() : Exception("Credenciais inválidas.");
public class ConflictException(string message) : Exception(message);
public record CustomerDto(Guid Id, string Name, string Email, string Role)
{ public static CustomerDto From(Customer c) => new(c.Id, c.Name, c.Email, c.Role); }
public record LoginResult(string AccessToken, int ExpiresIn, CustomerDto Customer);
public record OrderDto(Guid Id, Guid CustomerId, OrderStatus Status, DateTime CreatedAt, decimal Total, IReadOnlyList<OrderItem> Items)
{ public static OrderDto From(Order o) => new(o.Id, o.CustomerId, o.Status, o.CreatedAt, o.Total, o.Items); }
