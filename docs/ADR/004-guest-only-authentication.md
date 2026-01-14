# ADR-004: Guest-Only Authentication Model

**Status:** Accepted (Temporary - Demo Only)

**Date:** 2025-01-14 (Documented retrospectively)

**Context:** The Retail Monolith application requires customer identification to associate shopping carts and orders with users. A full authentication system (login, registration, sessions, identity management) adds significant complexity and is orthogonal to the core goal of demonstrating monolith modernization patterns.

---

## Decision

Implement a **guest-only authentication model** where all users operate under a single hardcoded customer identifier: `"guest"`.

---

## Rationale

### Why Guest-Only?

1. **Demo Simplicity**
   - Zero authentication setup or configuration
   - No user database tables (Users, Roles, Claims)
   - No login/logout UI or session management
   - Immediate focus on core business logic (products, cart, checkout, orders)

2. **Reduced Scope**
   - Authentication/authorization is a large topic with many implementation choices (cookies, JWT, OAuth, SAML)
   - Not the focus of monolith-to-microservices modernization demo
   - Can be added later without changing core business logic

3. **Faster Development**
   - No need to implement registration forms, password reset, email verification
   - No security considerations for password storage (hashing, salting)
   - No GDPR/privacy concerns (no personal data stored)

4. **Clear Limitation**
   - Explicitly demonstrates a "version 1" system
   - Obvious extension point for future enhancement
   - Shows evolution path from MVP to production

### Implementation Simplicity

Hardcoding `"guest"` as customer ID requires:
- No middleware or authentication handlers
- No session state or cookies
- No authorization policies
- Single line changes to add real authentication later

---

## Implementation Details

### Hardcoded Customer ID

The string `"guest"` is used throughout the codebase:

**Service Layer:**
```csharp
// CartService
await _cartService.AddToCartAsync("guest", productId);
await _cartService.GetCartWithLinesAsync("guest");

// CheckoutService
var order = await svc.CheckoutAsync("guest", "tok_test");
```

**Page Handlers:**
```csharp
// Products/Index.cshtml.cs (line 34)
.SingleOrDefaultAsync(c => c.CustomerId == "guest")

// Cart/Index.cshtml.cs (line 32)
var cart = await _cartService.GetCartWithLinesAsync("guest");
```

**API Endpoints:**
```csharp
// POST /api/checkout (Program.cs line 53)
var order = await svc.CheckoutAsync("guest", "tok_test");
```

### Data Model

**Cart.CustomerId:**
```csharp
public string CustomerId { get; set; } = "guest";
```
- Default value ensures all carts belong to "guest"

**Order.CustomerId:**
```csharp
public string CustomerId { get; set; } = "guest";
```
- All orders attributed to "guest" customer

### No User Entity

- No `User` or `Customer` table in database
- No user profile data stored
- `CustomerId` is just a string identifier (no FK constraint)

---

## Consequences

### Positive

1. **Zero Configuration:** No authentication setup required
2. **Immediate Usability:** Clone, run, use — no registration needed
3. **Simplified Testing:** No need to create test users or manage credentials
4. **Focus on Business Logic:** Demonstrates cart, checkout, orders without authentication distraction
5. **Clear Demo Scope:** Explicitly shows "this is not production-ready"

### Negative

1. **Single Shared Cart:** All users see and modify the same shopping cart
2. **No Privacy:** All orders visible to all users
3. **No Multi-User Support:** Cannot demonstrate user-specific features (order history, saved carts)
4. **Production Blocker:** Must implement real authentication before production deployment
5. **Unrealistic Demo:** Doesn't represent real e-commerce security requirements

### Data Integrity Issues

1. **Concurrent Access:** Multiple users modifying same cart simultaneously causes race conditions
2. **No Isolation:** User A adds item, User B sees it immediately
3. **Test Data Pollution:** Developers/testers share same data, causing confusion

### Security Implications

1. **No Access Control:** Anyone can access any order via API (`/api/orders/{id}`)
2. **No Authorization:** No concept of "my orders" vs "someone else's orders"
3. **Audit Trail Gap:** Cannot track which actual user performed actions
4. **No Rate Limiting Per User:** All actions attributed to single "guest"

---

## Alternatives Considered

### Alternative 1: ASP.NET Core Identity
- **Pros:**
  - Full-featured authentication system (login, registration, password reset)
  - Built-in security (password hashing, lockout, two-factor)
  - Integrates with EF Core
  - Industry-standard solution
- **Cons:**
  - Adds 5+ database tables (Users, Roles, Claims, etc.)
  - Requires UI for registration/login
  - Configuration complexity (password policies, email sender)
  - Significant scope increase for demo
- **Rejected:** Overkill for monolith modernization demo; focus should be on architecture, not auth

### Alternative 2: Cookie-Based Session ID
- **Pros:**
  - Simple anonymous user tracking
  - Each browser session gets unique ID
  - No login required, but per-session isolation
  - Better than single "guest"
- **Cons:**
  - Still no persistent user identity
  - Sessions expire, users lose carts
  - More complex than hardcoded "guest"
  - Requires session state configuration
- **Rejected:** Adds complexity without solving core issue (no real authentication)

### Alternative 3: JWT Tokens (Guest Tokens)
- **Pros:**
  - Stateless authentication
  - Each user gets unique token (even if anonymous)
  - Demonstrates modern auth pattern
  - Easier to scale
- **Cons:**
  - Token issuance endpoint needed
  - Client must store and send token
  - More complex than hardcoded approach
  - Still no real user identity
- **Rejected:** Too much infrastructure for demo; JWT is overkill without real users

### Alternative 4: IP Address as Customer ID
- **Pros:**
  - Automatic per-client isolation
  - No authentication needed
  - Somewhat realistic for anonymous users
- **Cons:**
  - IP addresses change (mobile networks, VPNs)
  - Multiple users behind NAT share same IP
  - Privacy concerns (tracking users by IP)
  - Not reliable identifier
- **Rejected:** Unreliable and creates false sense of isolation

### Alternative 5: No Customer Tracking (Stateless)
- **Pros:**
  - Simplest option
  - No sessions or user tracking
- **Cons:**
  - Cannot implement cart (no way to track user's items)
  - Cannot implement orders (no customer association)
  - Core features impossible to demonstrate
- **Rejected:** Cart and orders are essential to e-commerce demo

---

## Migration Path to Real Authentication

To add real authentication:

### Option 1: ASP.NET Core Identity

1. **Add NuGet Package:**
   ```xml
   <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="9.0.9" />
   ```

2. **Extend DbContext:**
   ```csharp
   public class AppDbContext : IdentityDbContext<IdentityUser>
   ```

3. **Add Identity Services:**
   ```csharp
   builder.Services.AddIdentity<IdentityUser, IdentityRole>()
       .AddEntityFrameworkStores<AppDbContext>()
       .AddDefaultTokenProviders();
   ```

4. **Add Authentication Middleware:**
   ```csharp
   app.UseAuthentication();
   app.UseAuthorization();
   ```

5. **Replace "guest" with Authenticated User:**
   ```csharp
   // In PageModel or API
   var customerId = User.Identity?.Name ?? "guest"; // Fallback to guest if not logged in
   ```

6. **Add Login/Register Pages:** Scaffold Identity UI or implement custom pages

7. **Migration:** Add Identity tables to database

### Option 2: External Identity Provider (Azure AD, Auth0, etc.)

1. **Configure OAuth/OIDC:**
   ```csharp
   builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
       .AddCookie()
       .AddOpenIdConnect("AzureAD", options => { /* config */ });
   ```

2. **Same User Replacement:** Replace "guest" with authenticated user's claim (e.g., email or subject ID)

3. **No Database Changes:** User data stored externally

### Option 3: API Key for Service-to-Service

If evolving to microservices:
- Cart Service issues API keys for authenticated services
- Replace "guest" with service identity
- Implement key validation middleware

---

## Known Issues

### Issue 1: Shared Cart Race Condition

**Scenario:**
1. User A adds Product 1 to cart
2. User B adds Product 2 to cart (same cart as User A)
3. User A proceeds to checkout — User B's item included

**Impact:** Unpredictable checkout totals, wrong items ordered

**Workaround:** Use application in single-user mode (local development only)

### Issue 2: Order Visibility

**Scenario:**
1. User A checks out, creates Order #1
2. User B can query `/api/orders/1` and see User A's order

**Impact:** No privacy or security

**Workaround:** None; intentional limitation

### Issue 3: No Order History UI

- `Orders/Index` page is placeholder
- Cannot distinguish "my orders" from "all orders" (all are "guest" orders)
- Would need authentication to implement meaningful order history

---

## Testing Implications

### Positive for Testing

1. **No Authentication Mock Needed:** Tests don't need to simulate logged-in users
2. **Simple Test Data:** All test operations use "guest"
3. **No Token Management:** No need to generate or refresh auth tokens in tests

### Negative for Testing

1. **Cannot Test Multi-User Scenarios:** No way to verify user isolation
2. **Authorization Tests Impossible:** Cannot test "User A cannot access User B's data"
3. **Race Conditions in Parallel Tests:** Multiple test runs interfere with shared "guest" cart

---

## Future Considerations

1. **Gradual Enhancement:**
   - Step 1: Add optional login (support both guest and authenticated users)
   - Step 2: Migrate guest cart to authenticated cart on login
   - Step 3: Deprecate guest mode for production

2. **Microservices Authentication:**
   - When decomposing to services, implement API gateway with authentication
   - Services receive user ID from gateway (not hardcoded)
   - Each service trusts gateway's user claims

3. **Customer Identity Service:**
   - Extract authentication to separate service
   - Cart/Checkout/Orders services receive customer ID from auth service
   - Enables centralized user management

---

## Documentation Notes

This ADR explicitly documents that **guest-only mode is a temporary, demo-only design decision**. Any production deployment or real-world usage requires proper authentication implementation.

**Warning to Implementers:** Do not deploy to production with guest-only mode. This is a security and privacy violation.

---

## References

- [ASP.NET Core Identity Documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity)
- [ASP.NET Core Authentication Overview](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- `Models/Cart.cs` - CustomerId default value
- `Models/Order.cs` - CustomerId default value
- `Services/CartService.cs` - Hardcoded "guest" usage
- `Services/CheckoutService.cs` - Hardcoded "guest" usage
