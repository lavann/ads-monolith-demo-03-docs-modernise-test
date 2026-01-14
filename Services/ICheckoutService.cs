using RetailMonolith.Models;

namespace RetailMonolith.Services
{
    public interface ICheckoutService
    {
        Task<Order> CheckoutAsync(string customerId, string paymentToken, CancellationToken ct = default);
    }
}
