using MediatR;
using OrderManagement.Domain;
using System.Text.Json;
namespace OrderManagement.Application;

public record RegisterCustomer(string Name, string Email, string Password) : IRequest<CustomerDto>;
public record Login(string Email, string Password) : IRequest<LoginResult>;
public record CreateProduct(string Name, decimal Price) : IRequest<Product>;
public record ItemInput(Guid ProductId, int Quantity);
public record CreateOrder(List<ItemInput> Items) : IRequest<OrderDto>;
public record ChangeStatus(Guid Id, OrderStatus Status) : IRequest<OrderDto>;
public record RequestReport() : IRequest<Guid>;

public sealed class CustomerCommands(IRepository<Customer> customers, IUnitOfWork uow, IPasswords passwords, ITokens tokens)
    : IRequestHandler<RegisterCustomer, CustomerDto>, IRequestHandler<Login, LoginResult>
{
    public async Task<CustomerDto> Handle(RegisterCustomer r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Password) || r.Password.Length is < 12 or > 128) throw new RuleException("Senha deve ter entre 12 e 128 caracteres.");
        var customer = new Customer(r.Name, r.Email, passwords.Hash(r.Password));
        if ((await customers.List(c => c.Email == customer.Email, 0, 1, ct)).Count != 0) throw new ConflictException("E-mail já cadastrado.");
        customers.Add(customer); await uow.Save(ct); return CustomerDto.From(customer);
    }
    public async Task<LoginResult> Handle(Login r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Email) || string.IsNullOrEmpty(r.Password) || r.Password.Length > 128) throw new UnauthorizedException();
        var email = r.Email.Trim().ToLowerInvariant();
        var customer = (await customers.List(c => c.Email == email, 0, 1, ct)).FirstOrDefault();
        if (customer is null || !passwords.Verify(customer.PasswordHash, r.Password)) throw new UnauthorizedException();
        return new(tokens.Create(customer), 3600, CustomerDto.From(customer));
    }
}
public sealed class ProductCommands(IRepository<Product> products, IUnitOfWork uow, ICurrentUser user) : IRequestHandler<CreateProduct, Product>
{
    public async Task<Product> Handle(CreateProduct r, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException();
        var p = new Product(r.Name, r.Price); products.Add(p); await uow.Save(ct); return p;
    }
}
public sealed class OrderCommands(IRepository<Order> orders, IRepository<Product> products, IRepository<Customer> customers,
    IRepository<OutboxMessage> outbox, IUnitOfWork uow, ICurrentUser user)
    : IRequestHandler<CreateOrder, OrderDto>, IRequestHandler<ChangeStatus, OrderDto>
{
    public async Task<OrderDto> Handle(CreateOrder r, CancellationToken ct)
    {
        if (r.Items is null || r.Items.Count is < 1 or > 100) throw new RuleException("Informe entre 1 e 100 itens.");
        _ = await customers.Get(user.Id, ct) ?? throw new MissingException();
        var items = new List<OrderItem>();
        foreach (var i in r.Items)
        {
            if (i is null) throw new RuleException("Item inválido.");
            items.Add(new(await products.Get(i.ProductId, ct) ?? throw new MissingException(), i.Quantity));
        }
        var order = new Order(user.Id, items); orders.Add(order);
        outbox.Add(new("Confirmation", JsonSerializer.Serialize(order.Id)));
        await uow.Save(ct); return OrderDto.From(order);
    }
    public async Task<OrderDto> Handle(ChangeStatus r, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException();
        var order = await orders.Get(r.Id, ct) ?? throw new MissingException();
        order.Advance(r.Status);
        outbox.Add(new("Status", JsonSerializer.Serialize(new StatusEvent(order.Id, order.CustomerId, order.Status, order.Version))));
        await uow.Save(ct); return OrderDto.From(order);
    }
}
public record StatusEvent(Guid OrderId, Guid CustomerId, OrderStatus Status, Guid Version);
public sealed class ReportCommands(IRepository<SalesReport> reports, IRepository<OutboxMessage> outbox, IUnitOfWork uow, ICurrentUser user) : IRequestHandler<RequestReport, Guid>
{
    public async Task<Guid> Handle(RequestReport r, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException();
        var report = new SalesReport(); reports.Add(report); outbox.Add(new("Report", JsonSerializer.Serialize(report.Id)));
        await uow.Save(ct); return report.Id;
    }
}
