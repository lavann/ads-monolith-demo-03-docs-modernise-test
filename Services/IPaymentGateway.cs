namespace RetailMonolith.Services
{
    //Mockable payment gateway interface

    public record PaymentRequest(decimal Amount, string Currency, string Token);
    public record PaymentResult(bool Succeeded, string? ProviderRef, string? Error);

    public interface IPaymentGateway
    {
        Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct = default);
    }
}
