using MediatR;
using OrderManagement.Domain;
namespace OrderManagement.Application;
public record ListProducts(int Page = 1) : IRequest<IReadOnlyList<Product>>;
public record ListCustomers(int Page = 1) : IRequest<IReadOnlyList<CustomerDto>>;
public record ListOrders(int Page = 1) : IRequest<IReadOnlyList<OrderDto>>;
public record GetOrder(Guid Id) : IRequest<OrderDto>;
public record GetReport(Guid Id) : IRequest<SalesReport>;
public static class Paging { public static int Skip(int page) => page is < 1 or > 100000 ? throw new RuleException("Página inválida.") : (page - 1) * 50; }
public sealed class CatalogQueries(IRepository<Product> products, IRepository<Customer> customers, ICurrentUser user)
    : IRequestHandler<ListProducts, IReadOnlyList<Product>>, IRequestHandler<ListCustomers, IReadOnlyList<CustomerDto>>
{
    public Task<IReadOnlyList<Product>> Handle(ListProducts r, CancellationToken ct) => products.List(null, Paging.Skip(r.Page), 50, ct);
    public async Task<IReadOnlyList<CustomerDto>> Handle(ListCustomers r, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException();
        return (await customers.List(null, Paging.Skip(r.Page), 50, ct)).Select(CustomerDto.From).ToList();
    }
}
public sealed class OrderQueries(IRepository<Order> orders, ICurrentUser user) : IRequestHandler<ListOrders, IReadOnlyList<OrderDto>>, IRequestHandler<GetOrder, OrderDto>
{
    public async Task<IReadOnlyList<OrderDto>> Handle(ListOrders r, CancellationToken ct)
        => (await orders.List(user.IsAdmin ? null : o => o.CustomerId == user.Id, Paging.Skip(r.Page), 50, ct)).Select(OrderDto.From).ToList();
    public async Task<OrderDto> Handle(GetOrder r, CancellationToken ct)
    {
        var o = await orders.Get(r.Id, ct) ?? throw new MissingException();
        if (!user.IsAdmin && o.CustomerId != user.Id) throw new MissingException();
        return OrderDto.From(o);
    }
}
public sealed class ReportQueries(IRepository<SalesReport> reports, ICurrentUser user) : IRequestHandler<GetReport, SalesReport>
{
    public async Task<SalesReport> Handle(GetReport r, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException();
        return await reports.Get(r.Id, ct) ?? throw new MissingException();
    }
}
