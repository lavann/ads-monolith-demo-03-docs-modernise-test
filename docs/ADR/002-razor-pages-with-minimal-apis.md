# ADR-002: Razor Pages with Minimal APIs

**Status:** Accepted (Implicit)

**Date:** 2025-01-14 (Documented retrospectively)

**Context:** The Retail Monolith application requires a presentation layer to serve both HTML pages for user interaction and JSON APIs for programmatic access. The application demonstrates a monolithic architecture that can evolve toward microservices.

---

## Decision

Use **ASP.NET Core Razor Pages** for HTML-based user interface and **Minimal APIs** for JSON-based HTTP endpoints.

---

## Rationale

### Why Razor Pages?

1. **Page-Centric Model**
   - Each page is self-contained (`.cshtml` view + `.cshtml.cs` PageModel)
   - Simpler than MVC for page-focused applications
   - Reduces ceremony for small-to-medium applications

2. **Conventional Routing**
   - File system-based routing: `/Pages/Products/Index.cshtml` maps to `/Products`
   - Intuitive URL structure
   - No explicit route configuration needed

3. **MVVM-Like Pattern**
   - PageModel class handles request logic
   - View (`.cshtml`) focuses on rendering
   - Clear separation of concerns within a page

4. **Built-In Features**
   - Anti-forgery tokens (CSRF protection) by default on POST requests
   - Model binding and validation
   - Dependency injection into PageModel constructors

### Why Minimal APIs?

1. **Lightweight Syntax**
   - Define endpoints inline in `Program.cs`
   - No controller classes required
   - Reduces boilerplate for simple APIs

2. **Performance**
   - Lower overhead compared to full MVC controllers
   - Direct routing without attribute scanning
   - Good fit for simple CRUD endpoints

3. **Simplicity for Demo**
   - Two endpoints (`POST /api/checkout`, `GET /api/orders/{id}`) are trivial
   - Overkill to create controller classes for such minimal API surface
   - Keeps `Program.cs` concise and readable

4. **Future API Gateway Pattern**
   - Minimal APIs signal these endpoints could be extracted to separate services
   - Clear separation from Razor Pages UI concerns

### Hybrid Approach Justification

- **UI-First Application:** Razor Pages serve primary customer-facing flows (browse, cart, checkout)
- **API-Second:** Minimal APIs provide programmatic access for future frontend (SPA, mobile) or service-to-service calls
- **Flexibility:** Demonstrates both HTML and JSON responses in single application

---

## Implementation Details

### Razor Pages Structure

```
/Pages
  /Cart
    Index.cshtml      # Cart view UI
    Index.cshtml.cs   # CartPageModel
  /Checkout
    Index.cshtml      # Checkout UI (placeholder)
    Index.cshtml.cs   # CheckoutPageModel (empty)
  /Orders
    Index.cshtml      # Orders UI (placeholder)
    Index.cshtml.cs   # OrdersPageModel (empty)
  /Products
    Index.cshtml      # Product listing UI
    Index.cshtml.cs   # ProductsPageModel
  /Shared
    _Layout.cshtml    # Master layout
  Index.cshtml        # Home page
  Error.cshtml        # Error page
  Privacy.cshtml      # Privacy page
```

**Page Examples:**

- **`/Products` (GET):** Display product catalog
- **`/Products` (POST):** Add item to cart
- **`/Cart` (GET):** Display shopping cart

### Minimal APIs

Defined in `Program.cs`:

```csharp
// POST /api/checkout - Execute checkout
app.MapPost("/api/checkout", async (ICheckoutService svc) =>
{
    var order = await svc.CheckoutAsync("guest", "tok_test");
    return Results.Ok(new { order.Id, order.Status, order.Total });
});

// GET /api/orders/{id} - Retrieve order by ID
app.MapGet("/api/orders/{id:int}", async (int id, AppDbContext db) =>
{
    var order = await db.Orders.Include(o => o.Lines)
        .SingleOrDefaultAsync(o => o.Id == id);
    return order is null ? Results.NotFound() : Results.Ok(order);
});
```

**Characteristics:**
- Lambda-based route handlers
- Dependency injection via parameters (`ICheckoutService`, `AppDbContext`)
- `Results` helper methods for responses

### Routing

- **Razor Pages:** Convention-based (`/Pages/Products/Index.cshtml` → `/Products`)
- **Minimal APIs:** Explicit mapping via `MapPost`, `MapGet`
- **Static Files:** `/wwwroot` served via `app.UseStaticFiles()`

---

## Consequences

### Positive

1. **Developer Productivity:** Razor Pages reduce boilerplate for page-based flows
2. **Simplicity:** Minimal APIs avoid controller overhead for small API surface
3. **Separation of Concerns:** UI pages and API endpoints are clearly separated
4. **Flexibility:** Can evolve either side independently (e.g., replace Razor with SPA, or extract APIs to services)
5. **Testability:** PageModels can be unit tested separately from views

### Negative

1. **Inconsistent Patterns:** Mix of Razor Pages (for UI) and Minimal APIs (for JSON) may confuse newcomers
2. **No API Versioning:** Minimal APIs lack built-in versioning strategy
3. **Limited API Documentation:** No automatic OpenAPI/Swagger generation (can be added)
4. **Placeholder Pages:** Several Razor Pages are empty (`Checkout`, `Orders`), creating incomplete user experience
5. **API Lacks Validation:** Minimal APIs have no request validation or error handling

### Architectural

1. **Tight Coupling in API:** `GET /api/orders/{id}` directly injects `AppDbContext` instead of using service layer
2. **Hardcoded Values:** `/api/checkout` uses hardcoded "guest" and "tok_test" (not production-ready)
3. **No Authentication:** Both UI and APIs lack authentication/authorization
4. **Future Migration Path:** Minimal APIs hint at service extraction points but share same database and business logic

---

## Alternatives Considered

### Alternative 1: MVC with Controllers
- **Pros:** 
  - Unified pattern for both UI and API
  - More familiar to developers from older ASP.NET
  - Better for complex routing
- **Cons:**
  - More boilerplate (controller classes, action methods)
  - Overkill for simple page-based flows
  - Heavier weight than Razor Pages or Minimal APIs
- **Rejected:** Unnecessary complexity for demo application

### Alternative 2: Blazor
- **Pros:**
  - C# on client and server
  - Rich interactive UI without JavaScript
  - Modern SPA-like experience
- **Cons:**
  - Steeper learning curve
  - Requires WebAssembly or SignalR understanding
  - Overcomplicates simple CRUD demo
- **Rejected:** Too much technology shift for modernization demo

### Alternative 3: Pure SPA (React/Angular) + Web API
- **Pros:**
  - Clean separation (frontend + backend)
  - Modern frontend experience
  - Better for microservices evolution
- **Cons:**
  - Two separate projects/technologies
  - Increased complexity for demo
  - Requires frontend build tooling
- **Rejected:** Wanted monolithic starting point; SPA could be future state

### Alternative 4: Minimal APIs Only (No Razor Pages)
- **Pros:**
  - Single presentation pattern
  - API-first design
  - Easy to extract services
- **Cons:**
  - No built-in UI for demonstration
  - Requires separate frontend or API testing tools
- **Rejected:** Wanted visual demonstration of user flows

---

## Implementation Gotchas

### Issue 1: Placeholder Pages
- **Problem:** `Checkout/Index` and `Orders/Index` have no functionality
- **Impact:** User flow is broken (cannot complete checkout via UI)
- **Current Workaround:** Use API endpoints instead
- **Future:** Implement full UI for checkout and order history

### Issue 2: Duplicate Cart Logic
- **Problem:** `Products/Index.cshtml.cs` has inline cart creation logic AND calls `CartService`
- **Location:** Lines 27-50 in `Pages/Products/Index.cshtml.cs`
- **Impact:** Redundant database operations
- **Fix Needed:** Remove inline logic, delegate fully to `CartService`

### Issue 3: Direct DbContext in API
- **Problem:** `/api/orders/{id}` injects `AppDbContext` directly
- **Location:** `Program.cs` line 58
- **Impact:** Breaks service layer abstraction
- **Fix Needed:** Create `OrderService` and use it instead

---

## Testing Strategy

### Razor Pages Testing
- **Unit Tests:** Test PageModel logic in isolation (mock services)
- **Integration Tests:** Use `WebApplicationFactory` to test full page request/response
- **Example:** Test `Products/IndexModel.OnPostAsync` adds item to cart

### Minimal API Testing
- **Integration Tests:** Use `WebApplicationFactory` to send HTTP requests
- **Example:** POST to `/api/checkout` and verify order creation

---

## Future Considerations

1. **Add Swagger/OpenAPI:** Document Minimal APIs for external consumers
2. **API Versioning:** Implement `/api/v1/...` when APIs stabilize
3. **Replace Razor with SPA:** Migrate to React/Blazor if rich interactivity needed
4. **Extract APIs to Microservices:** `/api/checkout` could become separate Checkout Service
5. **Complete Placeholder Pages:** Finish `Checkout` and `Orders` UI for full user experience

---

## References

- [ASP.NET Core Razor Pages Documentation](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/)
- [Minimal APIs Overview](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- `Program.cs` - Razor Pages and Minimal API registration
- `/Pages` directory - Razor Page implementations
