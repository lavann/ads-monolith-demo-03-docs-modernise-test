# Runbook - Retail Monolith

## Overview

This runbook provides operational guidance for building, running, and troubleshooting the Retail Monolith application. It covers local development setup, common commands, deployment considerations, and known issues.

---

## Prerequisites

### Required Software

1. **.NET 8 SDK**
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Verify installation:
     ```bash
     dotnet --version
     # Expected: 8.0.x or higher
     ```

2. **SQL Server LocalDB** (for Windows)
   - Included with Visual Studio 2022
   - Standalone installer: https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb
   - Verify installation:
     ```bash
     sqllocaldb info
     # Expected: MSSQLLocalDB listed
     ```

3. **Alternative: SQL Server (Any Edition)**
   - Express, Developer, or full SQL Server
   - Azure SQL Database also supported
   - Update connection string in `appsettings.Development.json`

### Optional Tools

- **Visual Studio 2022** or **Visual Studio Code** (recommended IDEs)
- **SQL Server Management Studio (SSMS)** for database inspection
- **Postman** or **curl** for API testing
- **Git** for version control

---

## Project Structure

```
RetailMonolith/
├── Data/                    # EF Core DbContext and design-time factory
│   ├── AppDbContext.cs
│   └── DesignTimeDbContextFactory.cs
├── Migrations/              # EF Core database migrations
│   └── 20251019185248_Initial.cs
├── Models/                  # Domain entities
│   ├── Product.cs
│   ├── InventoryItem.cs
│   ├── Cart.cs
│   └── Order.cs
├── Pages/                   # Razor Pages (UI)
│   ├── Products/
│   ├── Cart/
│   ├── Checkout/
│   └── Orders/
├── Services/                # Business logic services
│   ├── CartService.cs
│   ├── CheckoutService.cs
│   └── MockPaymentGateway.cs
├── wwwroot/                 # Static files (CSS, JS, images)
├── Program.cs               # Application entry point and configuration
├── appsettings.json         # Configuration (base)
├── appsettings.Development.json  # Dev-specific config (connection string)
└── RetailMonolith.csproj    # Project file
```

---

## Getting Started

### 1. Clone the Repository

```bash
git clone https://github.com/lavann/ads-monolith-demo-03-docs-modernise-test.git
cd ads-monolith-demo-03-docs-modernise-test
```

### 2. Restore Dependencies

```bash
dotnet restore
```

**Packages installed:**
- Microsoft.EntityFrameworkCore.SqlServer (9.0.9)
- Microsoft.EntityFrameworkCore.Design (9.0.9)
- Microsoft.AspNetCore.Diagnostics.HealthChecks (2.2.0)
- Microsoft.Extensions.Http.Polly (9.0.9)

### 3. Verify Database Connection

**Default Connection String (LocalDB):**
```
Server=(localdb)\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true
```

**Stored in:** `appsettings.Development.json`

**Test LocalDB:**
```bash
sqllocaldb start MSSQLLocalDB
sqllocaldb info MSSQLLocalDB
```

**Alternative: Use SQL Server**

Edit `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=RetailMonolith;User Id=sa;Password=YourPassword;TrustServerCertificate=True"
  }
}
```

### 4. Apply Database Migrations

**Option A: Automatic (Recommended for first run)**
The application automatically applies migrations on startup (see `Program.cs` line 27-28).

**Option B: Manual**
```bash
dotnet ef database update
```

**Verify Migration Applied:**
```bash
dotnet ef migrations list
# Expected output:
# 20251019185248_Initial (Applied)
```

### 5. Run the Application

```bash
dotnet run
```

**Expected Output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

**Access the Application:**
- HTTPS: https://localhost:5001
- HTTP: http://localhost:5000

**First Run Behavior:**
- Database created automatically (if not exists)
- Migrations applied
- 50 sample products seeded (if database empty)

---

## Common Commands

### Build

```bash
# Build in Debug mode
dotnet build

# Build in Release mode
dotnet build --configuration Release
```

### Run

```bash
# Run with default settings (Development environment)
dotnet run

# Run with specific environment
dotnet run --environment Production

# Run with custom launch profile
dotnet run --launch-profile "RetailMonolith"

# Watch mode (auto-restart on code changes)
dotnet watch run
```

### Database Management

#### View Existing Migrations
```bash
dotnet ef migrations list
```

#### Create New Migration
```bash
dotnet ef migrations add <MigrationName>
# Example: dotnet ef migrations add AddProductCategory
```

#### Apply Migrations
```bash
# Update to latest migration
dotnet ef database update

# Update to specific migration
dotnet ef database update <MigrationName>

# Rollback to previous migration
dotnet ef database update <PreviousMigrationName>
```

#### Drop Database (Reset)
```bash
dotnet ef database drop --force
```

#### Reseed Data
```bash
# 1. Drop and recreate database
dotnet ef database drop --force
dotnet ef database update

# 2. Run application (seeding happens automatically on startup)
dotnet run
```

#### Generate SQL Script from Migrations
```bash
dotnet ef migrations script --output migrations.sql
```

### Testing

**Note:** No automated tests currently exist in the repository.

To add tests:
```bash
# Create test project
dotnet new xunit -n RetailMonolith.Tests
dotnet sln add RetailMonolith.Tests/RetailMonolith.Tests.csproj

# Run tests (after implementation)
dotnet test
```

### Cleanup

```bash
# Clean build artifacts
dotnet clean

# Remove bin/ and obj/ folders
rm -rf bin obj
```

---

## API Endpoints

### Minimal APIs

#### POST /api/checkout
Execute checkout for "guest" customer.

**Request:**
```bash
curl -X POST https://localhost:5001/api/checkout \
  -H "Content-Type: application/json"
```

**Response:**
```json
{
  "id": 1,
  "status": "Paid",
  "total": 125.50
}
```

**Notes:**
- Hardcoded customer ID: "guest"
- Hardcoded payment token: "tok_test"
- No request body required (values hardcoded in endpoint)

#### GET /api/orders/{id}
Retrieve order details by ID.

**Request:**
```bash
curl https://localhost:5001/api/orders/1
```

**Response:**
```json
{
  "id": 1,
  "createdUtc": "2025-01-14T12:34:56Z",
  "customerId": "guest",
  "status": "Paid",
  "total": 125.50,
  "lines": [
    {
      "id": 1,
      "orderId": 1,
      "sku": "SKU-0001",
      "name": "Apparel Item 1",
      "unitPrice": 25.50,
      "quantity": 2
    }
  ]
}
```

**Error Response (Not Found):**
```json
HTTP 404 Not Found
```

### Health Check

#### GET /health
Check application health.

**Request:**
```bash
curl https://localhost:5001/health
```

**Response:**
```
Healthy
```

---

## User Interface Pages

### Page Routes

| Route         | Purpose                          | Status       |
|---------------|----------------------------------|--------------|
| `/`           | Home page                        | ✅ Working   |
| `/Products`   | Product catalog listing          | ✅ Working   |
| `/Cart`       | Shopping cart view               | ✅ Working   |
| `/Checkout`   | Checkout page                    | ⚠️ Placeholder |
| `/Orders`     | Order history                    | ⚠️ Placeholder |
| `/Privacy`    | Privacy policy                   | ✅ Working   |

### User Flows

#### Browse and Add to Cart
1. Navigate to `/Products`
2. Click "Add to Cart" on any product
3. Redirected to `/Cart` with item added

#### View Cart
1. Navigate to `/Cart`
2. View cart items and total
3. Click "Proceed to Checkout" (leads to placeholder page)

#### Checkout (API Only)
UI checkout is placeholder. Use API:
```bash
# 1. Add items to cart via UI
# 2. Execute checkout via API
curl -X POST https://localhost:5001/api/checkout

# 3. View order
curl https://localhost:5001/api/orders/1
```

---

## Configuration

### Connection Strings

**Development (appsettings.Development.json):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

**Production (Environment Variable):**
```bash
export ConnectionStrings__DefaultConnection="Server=tcp:myserver.database.windows.net,1433;Initial Catalog=RetailMonolith;Persist Security Info=False;User ID=myuser;Password=mypassword;Encrypt=True;"
```

### Logging

**Default Levels (appsettings.json):**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

**Override at Runtime:**
```bash
export Logging__LogLevel__Default=Debug
dotnet run
```

### Environment Variables

| Variable                               | Purpose                      | Default                |
|----------------------------------------|------------------------------|------------------------|
| `ASPNETCORE_ENVIRONMENT`               | Environment name             | `Development`          |
| `ASPNETCORE_URLS`                      | Listening URLs               | `https://localhost:5001;http://localhost:5000` |
| `ConnectionStrings__DefaultConnection` | Database connection string   | LocalDB connection     |

---

## Known Issues and Tech Debt

### Critical Issues

#### 1. Duplicate Cart Logic in Products Page
**Location:** `Pages/Products/Index.cshtml.cs` lines 27-50

**Problem:** Page handler manually creates cart and adds items, then also calls `CartService.AddToCartAsync`, resulting in redundant database operations.

**Workaround:** None; functionality works but performs unnecessary work.

**Fix:** Remove inline cart logic (lines 32-48), rely solely on `CartService`.

#### 2. Inventory Race Conditions
**Location:** `Services/CheckoutService .cs` lines 27-32

**Problem:** Optimistic concurrency during checkout. If two users checkout simultaneously, inventory may be oversold (both read quantity, both decrement, both save).

**Impact:** Critical in production under load.

**Workaround:** Single-user usage (demo only).

**Fix:** Implement pessimistic locking or use row versioning/optimistic concurrency tokens in EF Core.

#### 3. Guest-Only Mode
**Location:** Throughout codebase (hardcoded "guest" customer ID)

**Problem:** All users share same cart and orders. No user authentication or isolation.

**Impact:** Cannot deploy to production; no privacy or multi-user support.

**Workaround:** Use in single-user demo mode only.

**Fix:** Implement ASP.NET Core Identity or external auth provider.

#### 4. Mock Payment Gateway
**Location:** `Services/MockPaymentGateway.cs`

**Problem:** Always returns success; no real payment processing.

**Impact:** Cannot charge actual payments.

**Workaround:** None; intended for demo only.

**Fix:** Implement real payment gateway (Stripe, PayPal, etc.).

### Moderate Issues

#### 5. Direct DbContext in API Endpoint
**Location:** `Program.cs` line 58 (`GET /api/orders/{id}`)

**Problem:** API endpoint directly injects and queries `AppDbContext`, bypassing service layer.

**Impact:** Inconsistent architecture; harder to test and maintain.

**Fix:** Create `OrderService` and use it in endpoint.

#### 6. Placeholder Pages
**Location:** `Pages/Checkout/Index.cshtml`, `Pages/Orders/Index.cshtml`

**Problem:** Checkout and Orders pages have no functionality (empty OnGet handlers).

**Impact:** Incomplete user experience; users cannot complete checkout via UI.

**Workaround:** Use API endpoints (`POST /api/checkout`, `GET /api/orders/{id}`).

**Fix:** Implement full Razor Page functionality for checkout and order history.

#### 7. Hardcoded Payment Token in API
**Location:** `Program.cs` line 53

**Problem:** `/api/checkout` uses hardcoded "tok_test" payment token.

**Impact:** Cannot test different payment scenarios.

**Fix:** Accept payment token in request body.

#### 8. Auto-Seeding Runs on Every Startup
**Location:** `Program.cs` lines 24-29

**Problem:** Seeding logic executes on every application startup (though it checks if products exist first).

**Impact:** Adds startup delay; could accidentally seed production if database is empty.

**Fix:** Make seeding conditional on environment (only in Development) or create separate seeding command.

### Minor Issues

#### 9. No Input Validation
**Problem:** API endpoints and page handlers lack input validation.

**Impact:** Potential for invalid data or malformed requests causing exceptions.

**Fix:** Add data annotations and validation logic.

#### 10. No Repository Pattern
**Problem:** Services directly use `AppDbContext`.

**Impact:** Harder to unit test; tight coupling to EF Core.

**Fix:** Introduce repository interfaces and implementations.

#### 11. Missing Error Handling
**Problem:** Services throw raw exceptions (`InvalidOperationException`); APIs don't catch them.

**Impact:** 500 errors with stack traces exposed to clients.

**Fix:** Add global exception handling middleware and custom error responses.

#### 12. No Caching
**Problem:** Product catalog queried from database on every request.

**Impact:** Unnecessary database load for static data.

**Fix:** Add response caching or distributed cache (Redis).

---

## Troubleshooting

### Database Connection Errors

**Error:**
```
A network-related or instance-specific error occurred while establishing a connection to SQL Server.
```

**Solutions:**
1. Verify LocalDB is running:
   ```bash
   sqllocaldb info MSSQLLocalDB
   sqllocaldb start MSSQLLocalDB
   ```
2. Check connection string in `appsettings.Development.json`
3. Ensure SQL Server (if using) is running and accessible
4. Test connection with SSMS

### Migration Errors

**Error:**
```
Your target project 'RetailMonolith' doesn't match your migrations assembly 'RetailMonolith'.
```

**Solution:** Ensure `DesignTimeDbContextFactory` is correctly implemented.

**Error:**
```
Build failed.
```

**Solution:** Fix compilation errors before running migrations:
```bash
dotnet build
dotnet ef migrations add <Name>
```

### Port Already in Use

**Error:**
```
Failed to bind to address https://localhost:5001: address already in use.
```

**Solutions:**
1. Stop other instances of the application
2. Change port in `Properties/launchSettings.json`
3. Use `dotnet run --urls="https://localhost:5002"`

### HTTPS Certificate Issues

**Error:**
```
The SSL connection could not be established.
```

**Solutions:**
1. Trust development certificate:
   ```bash
   dotnet dev-certs https --trust
   ```
2. Use HTTP instead: `http://localhost:5000`

### Empty Product List

**Problem:** `/Products` page shows no products.

**Causes:**
1. Database not seeded
2. All products marked `IsActive = false`

**Solution:**
```bash
# Drop and recreate database to reseed
dotnet ef database drop --force
dotnet run
```

### Checkout API Fails

**Error:**
```
System.InvalidOperationException: Cart not found
```

**Cause:** No cart exists for "guest" customer.

**Solution:**
1. Add items to cart via `/Products` page first
2. Or create cart manually via UI before calling API

---

## Performance Tuning

### Database

1. **Add Indexes:**
   ```csharp
   // In AppDbContext.OnModelCreating
   modelBuilder.Entity<Cart>().HasIndex(c => c.CustomerId);
   modelBuilder.Entity<Order>().HasIndex(o => o.CustomerId);
   ```

2. **Batch Inventory Queries:**
   In `CheckoutService`, replace loop with single query:
   ```csharp
   var skus = cart.Lines.Select(l => l.Sku).ToList();
   var inventory = await _db.Inventory.Where(i => skus.Contains(i.Sku)).ToListAsync();
   ```

3. **Enable Query Logging (Development):**
   ```json
   {
     "Logging": {
       "LogLevel": {
         "Microsoft.EntityFrameworkCore.Database.Command": "Information"
       }
     }
   }
   ```

### Application

1. **Enable Response Caching:**
   ```csharp
   builder.Services.AddResponseCaching();
   app.UseResponseCaching();
   ```

2. **Add Output Caching (for static pages):**
   ```csharp
   builder.Services.AddOutputCache();
   app.UseOutputCache();
   ```

3. **Use Compiled Razor Views:**
   Already enabled in Release mode by default.

---

## Deployment

### Local IIS (Windows)

1. Publish application:
   ```bash
   dotnet publish -c Release -o ./publish
   ```

2. Create IIS site pointing to `./publish` folder

3. Update `appsettings.json` with production connection string

4. Ensure IIS has .NET 8 Hosting Bundle installed

### Azure App Service

1. **Deploy via Azure CLI:**
   ```bash
   az webapp up --name my-retail-app --resource-group my-rg --runtime "DOTNET|8.0"
   ```

2. **Configure Connection String:**
   ```bash
   az webapp config connection-string set \
     --name my-retail-app \
     --resource-group my-rg \
     --connection-string-type SQLAzure \
     --settings DefaultConnection="Server=tcp:..."
   ```

3. **Configure Environment:**
   ```bash
   az webapp config appsettings set \
     --name my-retail-app \
     --resource-group my-rg \
     --settings ASPNETCORE_ENVIRONMENT=Production
   ```

4. **Apply Migrations:**
   - Option A: Run migrations manually before deployment
   - Option B: Use startup migration (already enabled)
   - Option C: Use Azure DevOps pipeline to run `dotnet ef database update`

### Docker

**Dockerfile (create in project root):**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["RetailMonolith.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RetailMonolith.dll"]
```

**Build and Run:**
```bash
docker build -t retail-monolith .
docker run -p 8080:80 -e ConnectionStrings__DefaultConnection="Server=host.docker.internal;..." retail-monolith
```

---

## Monitoring and Diagnostics

### Health Checks

**Endpoint:** `/health`

**Usage:**
```bash
curl https://localhost:5001/health
```

**Configure Liveness/Readiness Probes (Kubernetes):**
```yaml
livenessProbe:
  httpGet:
    path: /health
    port: 80
  initialDelaySeconds: 30
  periodSeconds: 10
```

### Logging

**View Logs (Console):**
Logs output to stdout by default in Development mode.

**Configure Structured Logging (Serilog):**
```bash
dotnet add package Serilog.AspNetCore
```

**Application Insights (Azure):**
```bash
dotnet add package Microsoft.ApplicationInsights.AspNetCore
```

```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

---

## Security Checklist

Before deploying to production:

- [ ] Replace `MockPaymentGateway` with real payment processor
- [ ] Implement authentication (remove "guest" mode)
- [ ] Add authorization policies
- [ ] Store connection strings in Azure Key Vault or secure secret manager
- [ ] Enable HTTPS enforcement (already configured in `Program.cs`)
- [ ] Add rate limiting
- [ ] Implement input validation
- [ ] Add CORS policy (if needed for SPA)
- [ ] Review and harden HSTS settings
- [ ] Enable anti-forgery token validation on APIs (if stateful)
- [ ] Audit logging for sensitive operations

---

## Support and Resources

- **Repository:** https://github.com/lavann/ads-monolith-demo-03-docs-modernise-test
- **Documentation:** See `/docs` folder
  - HLD.md - High-Level Design
  - LLD.md - Low-Level Design
  - ADR/ - Architecture Decision Records
- **ASP.NET Core Docs:** https://learn.microsoft.com/en-us/aspnet/core/
- **EF Core Docs:** https://learn.microsoft.com/en-us/ef/core/

---

## Maintenance Tasks

### Regular Tasks
- Review database migration history
- Clean up old LocalDB instances: `sqllocaldb delete MSSQLLocalDB`
- Update NuGet packages: `dotnet list package --outdated`

### As Needed
- Add database backups (production)
- Review application logs for errors
- Monitor database size and performance
- Update .NET SDK to latest patch version

---

**Document Version:** 1.0  
**Last Updated:** 2025-01-14
