# ADR-003: Mock Payment Gateway

**Status:** Accepted (Temporary)

**Date:** 2025-01-14 (Documented retrospectively)

**Context:** The Retail Monolith application requires payment processing as part of the checkout flow. Real payment gateway integration (Stripe, PayPal, etc.) requires API keys, compliance considerations (PCI-DSS), sandbox environments, and webhook handling. For a demo application, this complexity is unnecessary and creates setup friction.

---

## Decision

Implement a **Mock Payment Gateway** (`MockPaymentGateway`) that always returns successful payment results without external API calls.

---

## Rationale

### Why Mock Payment?

1. **Demo Simplicity**
   - No API key configuration required
   - No external service dependencies
   - Application runs immediately without setup

2. **Avoid Compliance Overhead**
   - Real payment processing requires PCI-DSS compliance
   - Handling real credit cards introduces legal and security concerns
   - Mock eliminates these requirements for demo purposes

3. **Testing Convenience**
   - Predictable behavior (always succeeds)
   - No network latency or flakiness
   - Can simulate failures by modifying mock (if needed)

4. **Abstraction Design**
   - Interface (`IPaymentGateway`) allows easy swap to real implementation
   - Demonstrates dependency inversion principle
   - Prepares for future integration

### Why Interface Abstraction?

The design includes `IPaymentGateway` interface even though only mock exists:

1. **Testability:** Services can mock payment gateway in unit tests
2. **Future-Proofing:** Real gateway can be swapped in by implementing interface
3. **Dependency Injection:** Follows ASP.NET Core DI patterns
4. **Clean Architecture:** Keeps payment logic decoupled from business logic

---

## Implementation Details

### Interface Definition

`Services/IPaymentGateway.cs`:

```csharp
public record PaymentRequest(decimal Amount, string Currency, string Token);
public record PaymentResult(bool Succeeded, string? ProviderRef, string? Error);

public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct = default);
}
```

**Design Notes:**
- Uses C# records for immutable request/response models
- `PaymentRequest` contains amount, currency, and opaque token (simulating tokenized card data)
- `PaymentResult` includes success flag, provider reference, and optional error message
- Async signature (`Task<>`) prepares for real HTTP-based gateway

### Mock Implementation

`Services/MockPaymentGateway.cs`:

```csharp
public class MockPaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct = default)
    {
        // trivial success for hack; add a random fail to demo error path if you like
        return Task.FromResult(new PaymentResult(true, $"MOCK-{Guid.NewGuid():N}", null));
    }
}
```

**Behavior:**
- Always returns `Succeeded = true`
- Generates fake provider reference: `MOCK-{guid}` (e.g., `MOCK-a1b2c3d4e5f6...`)
- No error message (`null`)
- No actual network call (synchronous return wrapped in `Task.FromResult`)

### Dependency Injection

`Program.cs`:

```csharp
builder.Services.AddScoped<IPaymentGateway, MockPaymentGateway>();
```

- Registered as `Scoped` (same lifetime as DbContext)
- `CheckoutService` receives `IPaymentGateway` via constructor injection

### Usage in CheckoutService

`Services/CheckoutService .cs` (lines 35-36):

```csharp
var pay = await _payments.ChargeAsync(new(total, "GBP", paymentToken), ct);
var status = pay.Succeeded ? "Paid" : "Failed";
```

- Calls `ChargeAsync` with order total, currency "GBP", and payment token
- Sets order status to "Paid" if succeeded, "Failed" otherwise
- **Current Behavior:** Always sets status to "Paid" since mock always succeeds

---

## Consequences

### Positive

1. **Zero Setup:** No API keys, accounts, or configuration needed
2. **Reliable Testing:** No external failures; checkout flow always completes
3. **Fast Execution:** No network latency
4. **Clean Architecture:** Interface abstraction demonstrates good design
5. **Easy Replacement:** Real gateway can be swapped by implementing interface and changing DI registration

### Negative

1. **Not Production-Ready:** Cannot process real payments
2. **No Failure Testing:** Cannot easily test payment failure scenarios (would require code modification)
3. **False Confidence:** Developers might forget mock is in place and miss real integration issues
4. **No Validation:** Mock doesn't validate amount, currency, or token (accepts any input)
5. **Missing Features:** Real gateways have webhooks, refunds, 3D Secure, etc. — none of these are modeled

### Operational

1. **Deployment Risk:** Must not deploy to production with `MockPaymentGateway` registered
2. **Environment Configuration:** Need strategy to swap to real gateway per environment (dev = mock, prod = real)
3. **Monitoring Gap:** No real transactions to monitor or reconcile

---

## Alternatives Considered

### Alternative 1: Stripe Test Mode
- **Pros:**
  - Real gateway integration
  - Test card numbers available (4242 4242 4242 4242)
  - Demonstrates production-like flow
- **Cons:**
  - Requires Stripe account creation
  - API key management needed
  - Network dependency (flaky in tests)
  - Added complexity for demo
- **Rejected:** Too much setup friction; unnecessary for monolith demo

### Alternative 2: Payment Gateway Simulator (Hosted Mock Service)
- **Pros:**
  - More realistic than inline mock
  - Can simulate various responses (success, decline, timeout)
  - No real account needed
- **Cons:**
  - External dependency
  - Requires network connectivity
  - Adds deployment complexity
- **Rejected:** Overkill for simple demo; inline mock sufficient

### Alternative 3: No Payment Processing
- **Pros:**
  - Simplest option
  - No code needed
- **Cons:**
  - Incomplete checkout flow
  - Doesn't demonstrate integration patterns
  - Missing key business logic
- **Rejected:** Payment is core to e-commerce demo; must be present

### Alternative 4: Configurable Mock (Simulate Failures)
- **Pros:**
  - Can test failure paths
  - Controlled via configuration (e.g., fail percentage)
  - Still no external dependencies
- **Cons:**
  - More complex than current implementation
  - Non-deterministic behavior unless carefully controlled
- **Considered but Deferred:** Could enhance mock in future if failure testing needed

---

## Migration Path to Real Gateway

To replace with real payment gateway (e.g., Stripe):

1. **Implement `IPaymentGateway`:**
   ```csharp
   public class StripePaymentGateway : IPaymentGateway
   {
       private readonly StripeClient _client;
       
       public StripePaymentGateway(StripeClient client) => _client = client;
       
       public async Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct)
       {
           try {
               var charge = await _client.Charges.CreateAsync(new ChargeCreateOptions {
                   Amount = (long)(req.Amount * 100), // Convert to cents
                   Currency = req.Currency.ToLower(),
                   Source = req.Token
               }, cancellationToken: ct);
               
               return new PaymentResult(charge.Paid, charge.Id, null);
           }
           catch (StripeException ex) {
               return new PaymentResult(false, null, ex.Message);
           }
       }
   }
   ```

2. **Update DI Registration:**
   ```csharp
   builder.Services.AddScoped<IPaymentGateway, StripePaymentGateway>();
   ```

3. **Add Configuration:**
   - Store Stripe API key in `appsettings.json` or Key Vault
   - Inject `IConfiguration` into gateway constructor

4. **Handle Webhooks:**
   - Add endpoint to receive Stripe events (payment succeeded, failed, etc.)
   - Update order status asynchronously

5. **Environment-Specific Registration:**
   ```csharp
   if (builder.Environment.IsDevelopment())
       builder.Services.AddScoped<IPaymentGateway, MockPaymentGateway>();
   else
       builder.Services.AddScoped<IPaymentGateway, StripePaymentGateway>();
   ```

---

## Security Considerations

### Current (Mock) Security Issues
1. **Hardcoded Token:** API endpoint uses "tok_test" hardcoded value
2. **No Validation:** Mock accepts any token without checking format
3. **No Amount Validation:** Doesn't verify amount > 0 or within limits
4. **No Currency Validation:** Accepts any currency string

### Future (Real Gateway) Security Requirements
1. **API Key Security:** Store keys in secure configuration (Key Vault, environment variables)
2. **Token Handling:** Payment tokens should be single-use and short-lived
3. **TLS Required:** All payment communication must use HTTPS
4. **Webhook Verification:** Validate webhook signatures to prevent spoofing
5. **PCI Compliance:** Never log or store raw card data
6. **Rate Limiting:** Prevent payment API abuse

---

## Testing Strategy

### Current Testing (with Mock)
- **Unit Tests:** Easy to test checkout flow; payment always succeeds
- **Integration Tests:** Full checkout flow completes successfully
- **Example:** Verify order status is "Paid" after checkout

### Future Testing (with Real Gateway)
- **Use Test Mode:** Real gateway's test/sandbox environment
- **Mock Gateway for Unit Tests:** Still use `MockPaymentGateway` in unit tests
- **Integration Tests:** Use test API keys and test card numbers
- **Stub Webhooks:** Use local webhook testing tools (e.g., Stripe CLI)

---

## Known Issues

1. **Comment Typo:** Code comment says "add a random fail to demo error path if you like"
   - Suggests failure simulation was considered but not implemented
   - Current mock never fails

2. **Hardcoded "tok_test":** `/api/checkout` endpoint hardcodes payment token
   - Should accept token in request body
   - Not production-realistic

3. **No Failure Path:** `CheckoutService` sets status to "Failed" if payment fails, but this never happens with mock
   - Untested code path
   - May have bugs

---

## Future Enhancements

1. **Configurable Mock:**
   ```csharp
   public class ConfigurableMockPaymentGateway : IPaymentGateway
   {
       private readonly PaymentGatewayOptions _options;
       
       public async Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct)
       {
           if (_options.SimulateFailureRate > 0 && Random.Shared.NextDouble() < _options.SimulateFailureRate)
               return new PaymentResult(false, null, "Simulated failure");
           
           return new PaymentResult(true, $"MOCK-{Guid.NewGuid():N}", null);
       }
   }
   ```

2. **Multi-Gateway Support:**
   - Strategy pattern to support multiple payment providers
   - Select gateway based on currency or customer location

3. **Idempotency:**
   - Add idempotency key to prevent duplicate charges
   - Align with real gateway best practices

---

## References

- [Stripe Testing Documentation](https://stripe.com/docs/testing)
- [PCI Compliance Overview](https://www.pcisecuritystandards.org/)
- `Services/IPaymentGateway.cs` - Interface definition
- `Services/MockPaymentGateway.cs` - Mock implementation
- `Services/CheckoutService .cs` - Gateway usage
