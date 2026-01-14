# High-Level Design (HLD) - Retail Monolith

## System Overview

The Retail Monolith is an ASP.NET Core 8 web application implementing a traditional e-commerce system as a single deployable unit. It provides product catalog browsing, shopping cart management, checkout, and order processing capabilities through a Razor Pages frontend and minimal API endpoints.

**Technology Stack:**
- ASP.NET Core 8.0 (Razor Pages + Minimal APIs)
- Entity Framework Core 9.0.9
- SQL Server (LocalDB for development)
- .NET 8 SDK

## Architecture Overview

The application follows a layered monolithic architecture:

```
┌─────────────────────────────────────────┐
│         Presentation Layer              │
│  (Razor Pages + Minimal APIs)           │
│  /Pages, /api/checkout, /api/orders     │
└─────────────────────────────────────────┘
                  ↓
┌─────────────────────────────────────────┐
│         Service Layer                   │
│  CartService, CheckoutService,          │
│  PaymentGateway                         │
└─────────────────────────────────────────┘
                  ↓
┌─────────────────────────────────────────┐
│         Data Access Layer               │
│  AppDbContext (EF Core)                 │
└─────────────────────────────────────────┘
                  ↓
┌─────────────────────────────────────────┐
│         Data Store                      │
│  SQL Server / LocalDB                   │
│  (RetailMonolith database)              │
└─────────────────────────────────────────┘
```

## Components and Modules

### 1. Product Catalog Module
**Purpose:** Manages product information and inventory

**Components:**
- `Product` model: Core product entity with SKU, name, price, category
- `InventoryItem` model: Tracks stock levels per SKU
- `Products/Index` Razor Page: Product listing and "Add to Cart" UI

**Key Responsibilities:**
- Display active products to users
- Maintain product catalog data
- Track inventory quantities per SKU

### 2. Shopping Cart Module
**Purpose:** Manages user shopping cart state

**Components:**
- `Cart` model: Shopping cart entity with customer association
- `CartLine` model: Individual cart line items
- `CartService`: Business logic for cart operations
- `Cart/Index` Razor Page: Cart viewing UI

**Key Responsibilities:**
- Create and retrieve carts per customer
- Add products to cart
- Calculate cart totals
- Clear cart after checkout

### 3. Checkout Module
**Purpose:** Orchestrates the checkout process

**Components:**
- `CheckoutService`: Coordinates cart, inventory, payment, and order creation
- `IPaymentGateway` / `MockPaymentGateway`: Payment processing abstraction
- `Checkout/Index` Razor Page: Checkout UI (placeholder)
- `/api/checkout` Minimal API: Checkout endpoint

**Key Responsibilities:**
- Validate cart contents
- Reserve inventory
- Process payment
- Create orders
- Clear cart post-checkout

### 4. Orders Module
**Purpose:** Manages completed orders

**Components:**
- `Order` model: Order entity with status tracking
- `OrderLine` model: Order line items
- `Orders/Index` Razor Page: Order viewing UI (placeholder)
- `/api/orders/{id}` Minimal API: Order retrieval endpoint

**Key Responsibilities:**
- Store completed orders
- Track order status (Created, Paid, Failed, Shipped)
- Provide order history access

## Data Model

### Core Entities

**Product**
- Properties: Id, Sku (unique), Name, Description, Price, Currency, IsActive, Category
- Indexes: Unique on Sku

**InventoryItem**
- Properties: Id, Sku (unique), Quantity
- Indexes: Unique on Sku
- Note: Separate from Product to isolate inventory concerns

**Cart**
- Properties: Id, CustomerId
- Relationships: One-to-many with CartLine

**CartLine**
- Properties: Id, CartId, Sku, Name, UnitPrice, Quantity
- Relationships: Many-to-one with Cart

**Order**
- Properties: Id, CreatedUtc, CustomerId, Status, Total
- Relationships: One-to-many with OrderLine
- Status values: "Created", "Paid", "Failed", "Shipped"

**OrderLine**
- Properties: Id, OrderId, Sku, Name, UnitPrice, Quantity
- Relationships: Many-to-one with Order

### Database Schema
- Database Name: `RetailMonolith`
- ORM: Entity Framework Core 9.0.9
- Migration: Single initial migration (`20251019185248_Initial`)
- Connection: SQL Server with connection string configured in `appsettings.Development.json`

## External Dependencies

### SQL Server / LocalDB
- **Purpose:** Primary data store
- **Configuration:** LocalDB instance `(localdb)\MSSQLLocalDB` for development
- **Connection String:** Configurable via `appsettings.json` or environment variable
- **Deployment Note:** Can be swapped to Azure SQL for production

### Mock Payment Gateway
- **Current Implementation:** `MockPaymentGateway` always returns success
- **Interface:** `IPaymentGateway` provides abstraction for future integration
- **Future Integration:** Designed to be replaced with real payment provider (Stripe, etc.)

### Health Checks
- **Endpoint:** `/health`
- **Package:** Microsoft.AspNetCore.Diagnostics.HealthChecks 2.2.0
- **Purpose:** Basic application health monitoring

## Runtime Assumptions

### Startup Behavior
1. **Automatic Database Migration:** On application startup, the app automatically applies pending EF Core migrations via `await db.Database.MigrateAsync()`
2. **Automatic Seeding:** After migration, seeds 50 sample products with random categories, prices (£5-£105), and inventory (10-200 units) if database is empty
3. **Dependency Injection:** All services registered as scoped dependencies

### Execution Model
- **Web Server:** Kestrel (ASP.NET Core default)
- **Default Ports:** HTTPS on 5001, HTTP on 5000 (typical ASP.NET Core defaults)
- **Environment:** Development mode enables detailed errors and developer exception page

### Session/Authentication Model
- **Current State:** Guest-only mode
- **Customer Identification:** Hardcoded "guest" customer ID
- **No Authentication:** No user login, registration, or session management
- **Limitation:** All users share the same cart (single guest customer)

### Concurrency
- **Database:** MultipleActiveResultSets enabled in connection string
- **Inventory Management:** Optimistic concurrency (no locking mechanism)
- **Potential Issue:** Race conditions possible during checkout if multiple requests process simultaneously

### Data Consistency
- **Transaction Scope:** Each service method operates within a DbContext transaction boundary
- **Checkout Flow:** Inventory decrement, payment, and order creation happen in single transaction via `SaveChangesAsync`
- **Cart Clearing:** CartLines removed as part of checkout transaction

## Key Endpoints

### Razor Pages (HTML UI)
- `/` - Home page
- `/Products` - Product catalog listing
- `/Cart` - Shopping cart view
- `/Checkout` - Checkout page (placeholder UI)
- `/Orders` - Orders page (placeholder UI)
- `/Privacy` - Privacy policy page
- `/health` - Health check endpoint

### Minimal APIs (JSON)
- `POST /api/checkout` - Execute checkout for "guest" customer
  - Payload: Hardcoded customer ID and test payment token
  - Returns: Order summary (Id, Status, Total)
- `GET /api/orders/{id}` - Retrieve order details by ID
  - Returns: Full order with line items or 404

## Deployment Considerations

### Development Environment
- Requires .NET 8 SDK
- Uses SQL Server LocalDB (ships with Visual Studio)
- Database auto-created and seeded on first run
- HTTPS development certificate required

### Production Considerations
- Must configure production SQL Server connection string
- Remove/secure automatic seeding logic
- Implement proper authentication and customer management
- Replace MockPaymentGateway with real payment processor
- Configure HSTS and HTTPS redirection
- Set up proper logging and monitoring
- Consider connection pooling and performance tuning

## Limitations and Known Constraints

1. **Single-Tenant Design:** All users operate as "guest" customer
2. **No User Management:** No registration, login, or profile management
3. **Mock Payment:** Payment always succeeds, no real payment processing
4. **Basic Checkout UI:** Checkout page is placeholder, actual checkout via API
5. **No Order History UI:** Order viewing page is placeholder
6. **Race Conditions:** Potential inventory issues under concurrent load
7. **Automatic Seeding:** Runs on every startup if DB is empty (intended for demo)
8. **LocalDB Limitation:** Not suitable for production, single-user database

## Future Decomposition Readiness

The codebase includes hints for future microservices decomposition:

- **Service Boundaries:** Clear separation between Cart, Checkout, Products, Orders
- **Interface Abstractions:** `ICartService`, `ICheckoutService`, `IPaymentGateway` enable service replacement
- **API Endpoints:** Minimal APIs (e.g., `/api/checkout`) demonstrate potential service extraction points
- **Event Sourcing Hints:** Comment in `CheckoutService` suggests future event publishing: "publish events here: OrderCreated / PaymentProcessed / InventoryReserved"
- **Modular Structure:** Services and models organized by domain concept

This design is intentionally structured to facilitate eventual decomposition into microservices while currently operating as a monolith.
