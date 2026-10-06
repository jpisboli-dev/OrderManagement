namespace OrderManagement.Domain;

public class RuleException(string message) : Exception(message);
public abstract class Entity { public Guid Id { get; protected set; } = Guid.NewGuid(); }
public static class Guard
{
    public static string Text(string value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new RuleException($"{field} inválido.");
        return value.Trim();
    }
}
public sealed class Product : Entity
{
    private Product() { }
    public Product(string name, decimal price) { Name = Guard.Text(name, 160, "Nome"); if (price <= 0 || price > 1000000 || decimal.Round(price, 2) != price) throw new RuleException("Preço inválido: use até duas casas decimais."); Price = price; }
    public string Name { get; private set; } = "";
    public decimal Price { get; private set; }
}
public sealed class Customer : Entity
{
    private Customer() { }
    public Customer(string name, string email, string hash, string role = "Cliente")
    {
        Name = Guard.Text(name, 160, "Nome"); Email = Guard.Text(email, 254, "E-mail").ToLowerInvariant();
        if (!System.Net.Mail.MailAddress.TryCreate(Email, out var address) || address.Address != Email) throw new RuleException("E-mail inválido.");
        PasswordHash = hash; Role = role is "Admin" or "Cliente" ? role : throw new RuleException("Papel inválido.");
    }
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public string Role { get; private set; } = "Cliente";
}
public enum OrderStatus { Criado, Pago, EmPreparo, Enviado, Entregue }
public sealed class Order : Entity
{
    private Order() { }
    public Order(Guid customerId, IEnumerable<OrderItem> items)
    {
        CustomerId = customerId; Items = items.ToList();
        if (customerId == Guid.Empty || Items.Count is < 1 or > 100) throw new RuleException("Pedido deve conter entre 1 e 100 itens.");
        if (Items.Select(i => i.ProductId).Distinct().Count() != Items.Count) throw new RuleException("Produto repetido.");
    }
    public Guid CustomerId { get; private set; }
    public List<OrderItem> Items { get; private set; } = [];
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public Guid Version { get; private set; } = Guid.NewGuid();
    public decimal Total => Items.Sum(i => i.UnitPrice * i.Quantity);
    public void Advance(OrderStatus next)
    {
        if (Status == OrderStatus.Entregue || (int)next != (int)Status + 1) throw new RuleException("Transição de status inválida.");
        Status = next; Version = Guid.NewGuid();
    }
}
public sealed class OrderItem : Entity
{
    private OrderItem() { }
    public OrderItem(Product product, int quantity)
    {
        if (quantity is < 1 or > 10000) throw new RuleException("Quantidade deve estar entre 1 e 10000.");
        ProductId = product.Id; ProductName = product.Name; UnitPrice = product.Price; Quantity = quantity;
    }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = "";
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
}
public sealed class OutboxMessage(string kind, string payload) : Entity
{
    public string Kind { get; private set; } = kind;
    public string Payload { get; private set; } = payload;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
public sealed class SalesReport : Entity
{
    public DateTime RequestedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int PaidOrders { get; set; }
    public decimal Revenue { get; set; }
}
