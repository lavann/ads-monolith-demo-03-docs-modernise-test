using Microsoft.EntityFrameworkCore;
using RetailMonolith.Data;
using RetailMonolith.Models;

namespace RetailMonolith.Services
{
    public class CheckoutService : ICheckoutService
    {
        private readonly AppDbContext _db;
        private readonly IPaymentGateway _payments;

        public CheckoutService(AppDbContext db, IPaymentGateway payments)
        {
            _db = db; _payments = payments;
        }
        public async Task<Order> CheckoutAsync(string customerId, string paymentToken, CancellationToken ct = default)
        {
            // 1) pull cart
            var cart = await _db.Carts
                .Include(c => c.Lines)
                .SingleOrDefaultAsync(c => c.CustomerId == customerId, ct)
                ?? throw new InvalidOperationException("Cart not found");

            var total = cart.Lines.Sum(l => l.UnitPrice * l.Quantity);

            // 2) reserve/decrement stock (optimistic)
            foreach (var line in cart.Lines)
            {
                var inv = await _db.Inventory.SingleAsync(i => i.Sku == line.Sku, ct);
                if (inv.Quantity < line.Quantity) throw new InvalidOperationException($"Out of stock: {line.Sku}");
                inv.Quantity -= line.Quantity;
            }

            // 3) charge
            var pay = await _payments.ChargeAsync(new(total, "GBP", paymentToken), ct);
            var status = pay.Succeeded ? "Paid" : "Failed";

            // 4) create order
            var order = new Order { CustomerId = customerId, Status = status, Total = total };
            order.Lines = cart.Lines.Select(l => new OrderLine
            {
                Sku = l.Sku,
                Name = l.Name,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity
            }).ToList();

            _db.Orders.Add(order);

            // 5) clear cart
            _db.CartLines.RemoveRange(cart.Lines);
            await _db.SaveChangesAsync(ct);

            // (future) publish events here: OrderCreated / PaymentProcessed / InventoryReserved
            return order;
        }
    }
}
