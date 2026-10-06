using OrderManagement.Domain;
using Xunit;
namespace OrderManagement.Tests;
public class DomainTests
{
    [Fact] public void Order_requires_full_status_sequence()
    {
        var o = new Order(Guid.NewGuid(), [new(new Product("Teclado", 99.90m), 2)]);
        Assert.Equal(199.80m, o.Total);
        Assert.Throws<RuleException>(() => o.Advance(OrderStatus.Enviado));
        foreach (var next in new[] { OrderStatus.Pago, OrderStatus.EmPreparo, OrderStatus.Enviado, OrderStatus.Entregue }) o.Advance(next);
        Assert.Throws<RuleException>(() => o.Advance(OrderStatus.Criado));
        Assert.Throws<RuleException>(() => o.Advance(OrderStatus.Entregue));
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(10001)]
    public void Rejects_invalid_quantity(int quantity) => Assert.Throws<RuleException>(() => new OrderItem(new Product("Mouse", 10m), quantity));
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(1.001)]
    public void Rejects_invalid_price(decimal price) => Assert.Throws<RuleException>(() => new Product("Mouse", price));
    [Fact] public void Rejects_empty_and_duplicate_items()
    {
        Assert.Throws<RuleException>(() => new Order(Guid.NewGuid(), []));
        var p = new Product("Mouse", 10);
        Assert.Throws<RuleException>(() => new Order(Guid.NewGuid(), [new(p, 1), new(p, 2)]));
    }
}
