
namespace RetailMonolith.Services
{
    public class MockPaymentGateway : IPaymentGateway
    {
        public Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct = default)
        {
            // trivial success for hack; add a random fail to demo error path if you like
            return Task.FromResult(new PaymentResult(true, $"MOCK-{Guid.NewGuid():N}", null));
        }
    }
}
