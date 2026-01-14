# Low-Level Design (LLD) - Retail Monolith

## Overview
This document details the key classes, services, and request flows within the Retail Monolith application, identifying implementation details, coupling points, and areas requiring attention during modernization.

---

## Module Breakdown

### 1. Product Catalog Module

#### Key Classes

**`Product` (Models/Product.cs)**
```
- Id: int (PK)
- Sku: string (unique index)
- Name: string
- Description: string?
- Price: decimal
- Currency: string
- IsActive: bool
- Category: string?
```
**Purpose:** Core product entity representing items for sale.

**`InventoryItem` (Models/InventoryItem.cs)**
```
- Id: int (PK)
- Sku: string (unique index)
- Quantity: int
```
**Purpose:** Tracks stock levels separately from product catalog. Decoupled by SKU reference.

**`Products/IndexModel` (Pages/Products/Index.cshtml.cs)**
- **Dependencies:** `AppDbContext`, `ICartService`
- **OnGetAsync():** Loads active products from DB
- **OnPostAsync(int productId):** Handles "Add to Cart" action
- **Coupling Issue:** Page handler contains duplicate cart logic alongside service call

#### Request Flow: View Products
1. User navigates to `/Products`
2. `IndexModel.OnGetAsync()` queries `Products` table via EF Core
3. Filters for `IsActive == true`
4. Returns product list to Razor view
5. View renders products with "Add to Cart" buttons

#### Request Flow: Add to Cart
1. User clicks "Add to Cart" button (POST to `/Products`)
2. `IndexModel.OnPostAsync(int productId)` executes
3. **Issue:** Contains inline cart creation logic (lines 32-48)
   - Duplicates logic that exists in `CartService`
   - Directly manipulates DbContext
4. **Also calls:** `_cartService.AddToCartAsync("guest", productId)` (line 49)
   - Results in redundant cart operations
5. Redirects to `/Cart` page

**Hotspot:** Duplicate cart manipulation logic in page handler needs cleanup.

---

### 2. Shopping Cart Module

#### Key Classes

**`Cart` (Models/Cart.cs)**
```
- Id: int (PK)
- CustomerId: string (default: "guest")
- Lines: List<CartLine> (navigation property)
```

**`CartLine` (Models/CartLine.cs)**
```
- Id: int (PK)
- CartId: int (FK to Cart)
- Cart: Cart? (navigation property)
- Sku: string
- Name: string
- UnitPrice: decimal
- Quantity: int
```

**`ICartService` (Services/ICartService.cs)**
```csharp
public interface ICartService
{
    Task<Cart> GetOrCreateCartAsync(string customerId, CancellationToken ct = default);
    Task AddToCartAsync(string customerId, int productId, int quantity = 1, CancellationToken ct = default);
    Task<Cart> GetCartWithLinesAsync(string customerId, CancellationToken ct = default);
    Task ClearCartAsync(string customerId, CancellationToken ct = default);
}
```

**`CartService` (Services/CartService.cs)**
- **Dependencies:** `AppDbContext`
- **Scope:** Registered as Scoped in DI container

**Methods:**

1. **`AddToCartAsync(string customerId, int productId, int quantity)`**
   - Gets or creates cart
   - Fetches product by ID
   - Checks for existing line item with same SKU
   - Updates quantity if exists, adds new line if not
   - Saves changes to database
   - **Coupling:** Directly depends on `AppDbContext`, `Product`, `Cart`, `CartLine`

2. **`ClearCartAsync(string customerId)`**
   - Loads cart with lines (eager loading via `Include`)
   - Removes entire cart entity (cascade deletes lines)
   - Saves changes

3. **`GetCartWithLinesAsync(string customerId)`**
   - Queries cart with lines (eager loading)
   - Returns new empty cart if not found

4. **`GetOrCreateCartAsync(string customerId)`**
   - Queries cart with lines
   - Creates and persists new cart if not found
   - Always returns non-null cart

**Coupling Notes:**
- Service directly manipulates EF Core entities
- No repository abstraction layer
- CartService has knowledge of Product entity (cross-domain coupling)

#### Page Handler

**`Cart/IndexModel` (Pages/Cart/Index.cshtml.cs)**
- **Dependencies:** `ICartService`
- **State:** `Lines` property maps cart data to tuple: `(string Name, int Quantity, decimal Price)`
- **OnGetAsync():** Loads cart via service and projects to tuples
- **Note:** Read-only view, no POST handlers

#### Request Flow: View Cart
1. User navigates to `/Cart`
2. `IndexModel.OnGetAsync()` calls `_cartService.GetCartWithLinesAsync("guest")`
3. Projects `CartLine` entities to tuples for display
4. Calculates total: `Lines.Sum(line => line.Price * line.Quantity)`
5. Renders cart items and total

---

### 3. Checkout Module

#### Key Classes

**`Order` (Models/Order.cs)**
```
- Id: int (PK)
- CreatedUtc: DateTime (defaults to UtcNow)
- CustomerId: string (default: "guest")
- Status: string (default: "Created")
  Values: "Created" | "Paid" | "Failed" | "Shipped"
- Total: decimal
- Lines: List<OrderLine>
```

**`OrderLine` (Models/OrderLine.cs)**
```
- Id: int (PK)
- OrderId: int (FK to Order)
- Order: Order? (navigation property)
- Sku: string
- Name: string
- UnitPrice: decimal
- Quantity: int
```

**`IPaymentGateway` (Services/IPaymentGateway.cs)**
```csharp
public record PaymentRequest(decimal Amount, string Currency, string Token);
public record PaymentResult(bool Succeeded, string? ProviderRef, string? Error);

public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct = default);
}
```

**`MockPaymentGateway` (Services/MockPaymentGateway.cs)**
- **Implementation:** Returns success with mock transaction ID
- **Purpose:** Placeholder for real payment integration
- **Security Note:** Always succeeds, no validation

**`ICheckoutService` (Services/ICheckoutService.cs)**
```csharp
public interface ICheckoutService
{
    Task<Order> CheckoutAsync(string customerId, string paymentToken, CancellationToken ct = default);
}
```

**`CheckoutService` (Services/CheckoutService.cs)**
- **Dependencies:** `AppDbContext`, `IPaymentGateway`
- **Scope:** Registered as Scoped in DI container

**`CheckoutAsync(string customerId, string paymentToken)` - Critical Flow:**

```csharp
1. Load cart with lines via DbContext.Carts.Include(c => c.Lines)
   - Throws InvalidOperationException if cart not found
   - Calculates total: cart.Lines.Sum(l => l.UnitPrice * l.Quantity)

2. Reserve inventory (optimistic, no locking)
   - For each cart line:
     - Loads InventoryItem by SKU via DbContext.Inventory.SingleAsync()
     - Checks if quantity available
     - Throws InvalidOperationException if out of stock
     - Decrements inventory: inv.Quantity -= line.Quantity

3. Process payment
   - Calls _payments.ChargeAsync(new PaymentRequest(total, "GBP", paymentToken))
   - Sets status based on result: "Paid" if succeeded, "Failed" if not

4. Create order
   - Creates Order entity with status and total
   - Maps CartLines to OrderLines (copies SKU, Name, UnitPrice, Quantity)
   - Adds order to DbContext.Orders

5. Clear cart
   - Removes all CartLines via DbContext.CartLines.RemoveRange(cart.Lines)
   - Note: Cart entity itself remains (only lines removed)

6. Save changes
   - Single SaveChangesAsync() call commits all changes atomically

7. Return order
```

**Coupling Issues:**
- Service orchestrates across multiple domains: Cart, Inventory, Payment, Orders
- Direct DbContext manipulation (no repository layer)
- **Tight Coupling:** Knows about Cart, CartLine, InventoryItem, Order, OrderLine, PaymentGateway
- **Transaction Boundary:** All operations in single EF Core transaction
- **Concurrency Risk:** Inventory decrement has race condition (no pessimistic locking)

**Future Event Hints (Comment on line 54):**
```
// (future) publish events here: OrderCreated / PaymentProcessed / InventoryReserved
```

#### Page Handler

**`Checkout/IndexModel` (Pages/Checkout/Index.cshtml.cs)**
- **No logic:** Empty OnGet() method
- **UI:** Placeholder "Hello World Checkout" message
- **Note:** Actual checkout happens via `/api/checkout` endpoint

#### Request Flow: API Checkout
1. Client POSTs to `/api/checkout` (Minimal API endpoint in Program.cs)
2. Endpoint calls `CheckoutService.CheckoutAsync("guest", "tok_test")`
3. Service executes 7-step checkout flow (above)
4. Returns JSON: `{ Id, Status, Total }`

**Minimal API Definition (Program.cs lines 51-55):**
```csharp
app.MapPost("/api/checkout", async (ICheckoutService svc) =>
{
    var order = await svc.CheckoutAsync("guest", "tok_test");
    return Results.Ok(new { order.Id, order.Status, order.Total });
});
```

**Hotspots:**
- Hardcoded "guest" and "tok_test" values
- No request validation or error handling
- Directly exposes service exceptions to client

---

### 4. Orders Module

#### Key Classes

**Models:** See Order and OrderLine in Checkout section (shared models)

**`Orders/IndexModel` (Pages/Orders/Index.cshtml.cs)**
- **No logic:** Empty OnGet() method
- **UI:** Placeholder page

#### Request Flow: API Get Order
1. Client GETs `/api/orders/{id}` (Minimal API endpoint)
2. Endpoint queries DbContext.Orders directly (bypasses service layer)
3. Uses `Include(o => o.Lines)` for eager loading
4. Returns 404 if order not found, otherwise returns order JSON

**Minimal API Definition (Program.cs lines 58-63):**
```csharp
app.MapGet("/api/orders/{id:int}", async (int id, AppDbContext db) =>
{
    var order = await db.Orders.Include(o => o.Lines)
        .SingleOrDefaultAsync(o => o.Id == id);
    return order is null ? Results.NotFound() : Results.Ok(order);
});
```

**Coupling Issue:**
- API endpoint directly injects `AppDbContext` (no service abstraction)
- Breaks layered architecture pattern (presentation directly accesses data)

---

## Data Access Layer

### AppDbContext (Data/AppDbContext.cs)

**Responsibilities:**
- EF Core DbContext for all entity mappings
- Configures unique indexes on Product.Sku and InventoryItem.Sku
- Provides static seeding method

**DbSets:**
```csharp
public DbSet<Product> Products => Set<Product>();
public DbSet<InventoryItem> Inventory => Set<InventoryItem>();
public DbSet<Cart> Carts => Set<Cart>();
public DbSet<CartLine> CartLines => Set<CartLine>();
public DbSet<Order> Orders => Set<Order>();
public DbSet<OrderLine> OrderLines => Set<OrderLine>();
```

**OnModelCreating(ModelBuilder b):**
- Configures unique indexes:
  - `Product.Sku` (unique)
  - `InventoryItem.Sku` (unique)

**Static SeedAsync(AppDbContext db):**
- Checks if Products table is empty
- Seeds 50 products with:
  - SKUs: "SKU-0001" to "SKU-0050"
  - Names: "{Category} Item {N}"
  - Prices: £5-£105 (random)
  - Categories: "Apparel", "Footwear", "Accessories", "Electronics", "Home", "Beauty"
  - Currency: "GBP"
- Seeds matching InventoryItems with quantities 10-200 (random)
- Called automatically at startup (Program.cs line 28)

### DesignTimeDbContextFactory (Data/DesignTimeDbContextFactory.cs)

**Purpose:** Enables EF Core migrations tooling at design time

**Implementation:**
- Hardcoded connection string: `Server=(localdb)\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true`
- Used by `dotnet ef` commands (migrations add, database update, etc.)

---

## Key Request Flows Summary

### 1. Browse Products and Add to Cart
```
User → /Products (GET)
  → IndexModel.OnGetAsync()
  → DbContext.Products.Where(p => p.IsActive)
  → Render product list

User → /Products (POST, productId)
  → IndexModel.OnPostAsync(productId)
  → [Duplicate cart logic - creates cart, adds line]
  → CartService.AddToCartAsync("guest", productId)
  → DbContext.SaveChangesAsync()
  → Redirect to /Cart
```

### 2. View Cart
```
User → /Cart (GET)
  → IndexModel.OnGetAsync()
  → CartService.GetCartWithLinesAsync("guest")
  → DbContext.Carts.Include(c => c.Lines)
  → Project to tuples, calculate total
  → Render cart
```

### 3. Checkout (via API)
```
Client → POST /api/checkout
  → CheckoutService.CheckoutAsync("guest", "tok_test")
    Step 1: Load cart from DB
    Step 2: Validate and decrement inventory
    Step 3: Call PaymentGateway.ChargeAsync()
    Step 4: Create Order entity
    Step 5: Remove CartLines
    Step 6: SaveChangesAsync() [atomic transaction]
  → Return { Id, Status, Total }
```

### 4. Retrieve Order (via API)
```
Client → GET /api/orders/{id}
  → Direct DbContext query
  → DbContext.Orders.Include(o => o.Lines).SingleOrDefaultAsync(id)
  → Return order JSON or 404
```

---

## Areas of Coupling and Hotspots

### Critical Coupling Issues

1. **Products/IndexModel Duplicate Logic**
   - **Location:** `Pages/Products/Index.cshtml.cs` lines 27-50
   - **Issue:** Page handler manually creates cart and adds lines, then also calls `CartService.AddToCartAsync`
   - **Impact:** Redundant database operations, inconsistent cart management
   - **Fix Needed:** Remove inline cart logic, rely solely on service

2. **CheckoutService Cross-Domain Coupling**
   - **Location:** `Services/CheckoutService .cs`
   - **Issue:** Single service knows about and manipulates 5+ entity types (Cart, CartLine, InventoryItem, Order, OrderLine)
   - **Impact:** God service anti-pattern, difficult to test and maintain
   - **Risk:** Changes to any domain model ripple through checkout logic
   - **Future:** Candidate for decomposition into separate services

3. **Direct DbContext Injection in APIs**
   - **Location:** `Program.cs` line 58 (`/api/orders/{id}`)
   - **Issue:** Minimal API endpoint directly injects and uses `AppDbContext`
   - **Impact:** Breaks service layer abstraction, data access logic in presentation
   - **Inconsistency:** Other endpoints use services, this one doesn't

4. **Hardcoded "guest" Customer ID**
   - **Locations:** Throughout codebase
   - **Issue:** No authentication or customer differentiation
   - **Impact:** All users share same cart and orders
   - **Risk:** Cannot scale to multi-user scenarios

5. **Inventory Race Conditions**
   - **Location:** `CheckoutService.cs` lines 27-32
   - **Issue:** Optimistic concurrency (read → decrement → save) without locking
   - **Impact:** Multiple concurrent checkouts may oversell inventory
   - **Example:** Two users checkout last item simultaneously, both succeed

### Tech Debt Hotspots

1. **No Repository Pattern**
   - Services directly use `AppDbContext`
   - Difficult to unit test (requires in-memory database)
   - Tight coupling to EF Core

2. **Minimal Error Handling**
   - Services throw raw exceptions (`InvalidOperationException`)
   - No custom exception types or error codes
   - API endpoints don't catch or translate exceptions

3. **No Validation Layer**
   - No input validation on API endpoints
   - No domain validation rules
   - Relies on database constraints only

4. **Placeholder UIs**
   - `Checkout/Index` and `Orders/Index` are empty placeholders
   - Actual functionality only available via API
   - Inconsistent user experience

5. **Auto-Seeding in Production**
   - Seeding logic runs at startup (Program.cs line 28)
   - Should be conditional on environment
   - Could accidentally seed production database

6. **Payment Gateway Always Succeeds**
   - `MockPaymentGateway` returns success for all charges
   - No failure path testing
   - No validation of payment token

### Positive Coupling Patterns

1. **Interface Abstractions**
   - `ICartService`, `ICheckoutService`, `IPaymentGateway`
   - Enables dependency injection and testing
   - Facilitates future service replacement

2. **Domain Model Separation**
   - Clear entity definitions in `/Models`
   - Separate concerns (Product ≠ InventoryItem, Cart ≠ Order)
   - Prepares for bounded context separation

3. **Service Registration in DI**
   - All services registered in Program.cs
   - Scoped lifetime for database operations
   - Follows ASP.NET Core best practices

---

## Testing Considerations

### Testability Issues

1. **DbContext Dependency**
   - Services require `AppDbContext`
   - Must use in-memory database or mocked DbContext for unit tests
   - Integration tests needed to verify EF Core queries

2. **Hardcoded Values**
   - "guest", "tok_test", "GBP" scattered throughout
   - Difficult to test different scenarios

3. **No Repositories**
   - Cannot easily mock data access
   - Services tightly coupled to database

### Testable Components

1. **Interface-Based Services**
   - `ICartService`, `ICheckoutService`, `IPaymentGateway` can be mocked
   - Page handlers can be tested with service mocks

2. **Models**
   - POCOs without behavior
   - Easy to instantiate and assert

3. **Minimal APIs**
   - Can be tested with WebApplicationFactory
   - Integration tests straightforward

---

## Performance Considerations

### Potential Bottlenecks

1. **N+1 Query Risk**
   - `CheckoutService` loops through cart lines and loads inventory one-by-one
   - Should batch-load inventory items

2. **Missing Indexes**
   - Only indexes on SKU fields
   - May need indexes on CustomerId for cart/order queries
   - No composite indexes

3. **Eager Loading Everywhere**
   - All cart/order queries use `.Include()`
   - Loads child collections even when not needed
   - Consider projection for read-only views

4. **No Caching**
   - Product catalog hit on every request
   - No distributed cache (Redis, etc.)
   - All data fetched from database

5. **Synchronous Seeding**
   - Blocks startup while seeding 50 products
   - Not an issue for demo, but scales poorly

---

## Security Concerns

1. **SQL Injection:** Protected by EF Core parameterization
2. **No Authentication:** Guest-only mode, no user verification
3. **No Authorization:** All data accessible to all users
4. **Payment Token:** Passed as plain string, no encryption
5. **No Rate Limiting:** APIs unprotected from abuse
6. **CSRF:** Razor Pages use anti-forgery tokens (good), APIs do not

---

## Summary

The Retail Monolith follows a straightforward layered architecture with clear domain boundaries, but exhibits typical monolithic coupling issues:

- **Strong Points:** Interface abstractions, dependency injection, clean domain models
- **Weak Points:** Direct DbContext usage, duplicate logic, hardcoded values, minimal validation
- **Decomposition Readiness:** Services are logically separated and could be extracted, but share database and lack event-driven communication

The codebase is well-structured for a monolith and contains hints of future microservices patterns, making it a good candidate for incremental modernization.
