# ADR-008: Database-per-Service Pattern with Phased Migration

**Status:** Accepted

**Date:** 2026-01-14

**Context:** The current monolith uses a single shared SQL Server database (RetailMonolith) for all entities (Product, Inventory, Cart, Order). As we extract microservices, we must decide the data access strategy. Continuing with a shared database maintains tight coupling and prevents independent service evolution. A database-per-service approach enables true service autonomy but introduces data consistency and migration challenges.

---

## Decision

Implement the **Database-per-Service pattern** using a **phased migration approach**:

1. **Phase 1 (Shared Database with Logical Separation):** Services access shared database but only their designated tables
2. **Phase 2 (Schema Separation):** Create separate schemas per service within the same database instance
3. **Phase 3 (Physical Separation):** Migrate each service to dedicated database instances

---

## Rationale

### Why Database-per-Service?

**Core Principle:** Each microservice owns its data and exposes it only via its API.

**Benefits:**

1. **Service Autonomy**
   - Services can evolve database schemas independently
   - No coordination needed for schema changes
   - Reduces deployment dependencies

2. **Technology Flexibility**
   - Each service can choose optimal database technology
   - Cart Service: Redis (fast, session-based)
   - Product Catalog: SQL Server (relational, ACID)
   - Analytics: NoSQL (eventual consistency, high write throughput)

3. **Failure Isolation**
   - Database failure affects only one service, not entire system
   - Independent scaling (scale databases independently)
   - Blast radius containment

4. **Team Ownership**
   - Teams own full stack (service + database)
   - No shared DBA bottleneck
   - Faster iteration

**Challenges:**

1. **Data Consistency**
   - No ACID transactions across services
   - Eventual consistency via saga pattern or events
   - Referential integrity cannot be enforced at database level

2. **Data Duplication**
   - Reference data (products) replicated across services
   - Requires synchronization mechanisms
   - Storage overhead

3. **Querying Complexity**
   - Cross-service queries require API calls or data replication
   - Reporting requires data aggregation from multiple services
   - Potential for N+1 query problems

4. **Migration Complexity**
   - Data migration from shared to separate databases
   - Downtime or dual-write period required
   - Risk of data inconsistency during migration

### Why Phased Migration?

**Big-bang database split is high-risk.** Phased approach allows:
- Validate service extraction before data migration
- Rollback capability at each phase
- Incremental risk (one service at a time)
- Learn and adjust strategy based on early phases

---

## Implementation Details

### Phase 1: Shared Database with Logical Separation

**Duration:** Phases 1-2 of service extraction (Cart, Product Catalog)

**Approach:** Services access same SQL Server database but adhere to strict table ownership rules.

**Configuration:**
```csharp
// CartService appsettings.json
{
  "ConnectionStrings": {
    "CartDb": "Server=retailmonolith.database.windows.net;Database=RetailMonolith;User Id=cart_service;Password=...;"
  }
}

// CartService DbContext
public class CartDbContext : DbContext
{
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartLine> CartLines => Set<CartLine>();
    
    // Explicitly DO NOT include Product, Order, etc.
    // Enforces service boundary at code level
}
```

**Table Ownership:**
| Service | Tables Owned | Access Type |
|---------|-------------|-------------|
| Cart Service | Cart, CartLine | Read/Write |
| Product Catalog Service | Product, InventoryItem | Read/Write |
| Checkout Service | None (orchestrator) | No database |
| Order Service | Order, OrderLine | Read/Write |
| Monolith (temporary) | All tables | Read/Write (deprecated) |

**Enforcement:**
- Code reviews ensure services only access owned tables
- Different database users per service with table-level permissions:
  ```sql
  -- Create cart_service user with limited permissions
  CREATE USER cart_service WITH PASSWORD = 'SecurePassword123!';
  GRANT SELECT, INSERT, UPDATE, DELETE ON Cart TO cart_service;
  GRANT SELECT, INSERT, UPDATE, DELETE ON CartLine TO cart_service;
  -- NO GRANT on Product, Order, etc.
  ```

**Benefits:**
- ✅ Low risk (no data movement)
- ✅ Easy rollback (revert to monolith)
- ✅ Validates service logic before committing to data migration

**Drawbacks:**
- ❌ Shared database is bottleneck (cannot scale independently)
- ❌ Schema changes still require coordination
- ❌ Potential for accidental cross-service data access (mitigated by permissions)

---

### Phase 2: Schema Separation (Optional Intermediate Step)

**Duration:** After Phase 2-3 of service extraction

**Approach:** Create separate schemas within same SQL Server database.

**Configuration:**
```sql
-- Create schemas
CREATE SCHEMA cart;
CREATE SCHEMA product;
CREATE SCHEMA [order];

-- Move tables to schemas
ALTER SCHEMA cart TRANSFER dbo.Cart;
ALTER SCHEMA cart TRANSFER dbo.CartLine;
ALTER SCHEMA product TRANSFER dbo.Product;
ALTER SCHEMA product TRANSFER dbo.InventoryItem;
ALTER SCHEMA [order] TRANSFER dbo.[Order];
ALTER SCHEMA [order] TRANSFER dbo.OrderLine;

-- Update permissions
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::cart TO cart_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::product TO product_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[order] TO order_service;
```

**DbContext Update:**
```csharp
// CartService DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDefaultSchema("cart");
    
    modelBuilder.Entity<Cart>().ToTable("Cart", "cart");
    modelBuilder.Entity<CartLine>().ToTable("CartLine", "cart");
}
```

**Benefits:**
- ✅ Clearer boundary (visual separation in DB tools)
- ✅ Easier to audit who accesses what
- ✅ Prepares for physical separation

**Drawbacks:**
- ❌ Still shared database (same scaling/failure constraints)
- ❌ Additional migration step (may skip directly to Phase 3)

---

### Phase 3: Physical Database Separation

**Duration:** After Phase 3-4 of service extraction (Checkout, Order Services operational)

**Approach:** Migrate each service's tables to dedicated database instances.

**Target State:**
```
Cart Service → CartServiceDB (SQL Server or Redis)
Product Catalog Service → ProductCatalogDB (SQL Server)
Order Service → OrderServiceDB (SQL Server)
Checkout Service → No database (orchestrator only)
```

**Migration Process (Example: Cart Service)**

#### Step 1: Provision New Database
```bash
# Azure SQL Database
az sql db create \
  --resource-group retail-services-rg \
  --server retail-sqlserver \
  --name CartServiceDB \
  --service-objective S2 \
  --backup-storage-redundancy Zone
```

#### Step 2: Schema Migration
```bash
# Generate migration SQL from CartDbContext
dotnet ef migrations script --output cart-schema.sql --context CartDbContext

# Apply to new database
sqlcmd -S retailsqlserver.database.windows.net -d CartServiceDB -U admin -P password -i cart-schema.sql
```

#### Step 3: Data Migration (ETL)
```csharp
// CartDataMigrationTool/Program.cs
public class CartDataMigrator
{
    public async Task MigrateAsync(string sourceConn, string targetConn)
    {
        var source = new SqlConnection(sourceConn);
        var target = new SqlConnection(targetConn);
        
        await source.OpenAsync();
        await target.OpenAsync();
        
        // Copy Carts
        var carts = await source.QueryAsync<Cart>("SELECT * FROM Cart");
        await target.ExecuteAsync(
            "INSERT INTO Cart (Id, CustomerId) VALUES (@Id, @CustomerId)", 
            carts);
        
        // Copy CartLines
        var cartLines = await source.QueryAsync<CartLine>("SELECT * FROM CartLine");
        await target.ExecuteAsync(
            "INSERT INTO CartLine (Id, CartId, Sku, Name, UnitPrice, Quantity) " +
            "VALUES (@Id, @CartId, @Sku, @Name, @UnitPrice, @Quantity)", 
            cartLines);
        
        Console.WriteLine($"Migrated {carts.Count()} carts and {cartLines.Count()} cart lines");
    }
}
```

#### Step 4: Dual-Write Period
**Strategy:** Write to both databases during transition to ensure data consistency.

```csharp
// CartService.cs (temporary dual-write)
public async Task AddToCartAsync(string customerId, int productId, int quantity)
{
    // Write to new database (primary)
    using var newDbContext = _newDbContextFactory.CreateDbContext();
    var cart = await newDbContext.Carts.FindAsync(customerId);
    // ... add logic
    await newDbContext.SaveChangesAsync();
    
    // Write to old database (fallback, temporary)
    try
    {
        using var oldDbContext = _oldDbContextFactory.CreateDbContext();
        var oldCart = await oldDbContext.Carts.FindAsync(customerId);
        // ... same logic
        await oldDbContext.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Dual-write to old database failed (non-critical)");
    }
}
```

#### Step 5: Validation Period
- Run both databases in parallel for 1-2 weeks
- Compare data between old and new (reconciliation script)
- Monitor error rates and latency

#### Step 6: Cutover
```csharp
// Remove dual-write, use only new database
builder.Services.AddDbContext<CartDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("CartDb")));  // New database connection string
```

#### Step 7: Decommission Old Tables
```sql
-- After 30 days with no rollback, drop old tables
DROP TABLE Cart;
DROP TABLE CartLine;
```

---

## Handling Cross-Service Data Access

### Challenge: Services need data from other services

**Example:** Cart Service needs product names and prices (owned by Product Catalog Service).

### Solution 1: API Calls (Recommended for Phase 1-2)

**Pattern:** Service-to-service HTTP calls

```csharp
// CartService calls ProductCatalogService
public async Task AddToCartAsync(string customerId, int productId)
{
    var product = await _productClient.GetProductAsync(productId);
    if (product == null)
        throw new InvalidOperationException("Product not found");
    
    var cart = await GetOrCreateCartAsync(customerId);
    cart.Lines.Add(new CartLine
    {
        Sku = product.Sku,
        Name = product.Name,  // Snapshot at time of add
        UnitPrice = product.Price,
        Quantity = 1
    });
    
    await _dbContext.SaveChangesAsync();
}
```

**Pros:**
- ✅ Single source of truth (Product Service)
- ✅ Real-time data (always current price)

**Cons:**
- ❌ Increased latency (network call)
- ❌ Coupling (Cart Service depends on Product Service availability)

**Mitigation:**
- Circuit breaker (failover to cached data)
- Cache product data locally (5-minute TTL)

### Solution 2: Data Replication (Event-Driven)

**Pattern:** Subscribe to ProductUpdated events, replicate data locally

```csharp
// CartService subscribes to ProductUpdatedEvent
public class ProductUpdatedEventHandler : IEventHandler<ProductUpdatedEvent>
{
    private readonly CartDbContext _dbContext;

    public async Task HandleAsync(ProductUpdatedEvent evt)
    {
        // Update local product cache table
        var product = await _dbContext.ProductCache.FindAsync(evt.Sku);
        if (product == null)
        {
            product = new ProductCache { Sku = evt.Sku };
            _dbContext.ProductCache.Add(product);
        }
        
        product.Name = evt.Name;
        product.Price = evt.Price;
        product.IsActive = evt.IsActive;
        product.LastUpdated = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync();
    }
}

// CartService uses local cache (no API call needed)
public async Task AddToCartAsync(string customerId, string sku)
{
    var product = await _dbContext.ProductCache.FindAsync(sku);
    if (product == null)
        throw new InvalidOperationException("Product not found");
    
    // Use cached product data
    cart.Lines.Add(new CartLine
    {
        Sku = product.Sku,
        Name = product.Name,
        UnitPrice = product.Price,
        Quantity = 1
    });
    
    await _dbContext.SaveChangesAsync();
}
```

**Pros:**
- ✅ Low latency (local read)
- ✅ Resilient (works even if Product Service down)

**Cons:**
- ❌ Eventual consistency (stale data possible)
- ❌ Storage overhead (duplicate data)

**Use Cases:**
- Read-heavy data (products)
- Data changes infrequently
- Staleness acceptable (5-minute lag acceptable for product prices)

### Solution 3: CQRS with Read Models

**Pattern:** Separate read and write models, optimize for queries

**Future Enhancement (Phase 4+):**
- Dedicated read database (e.g., Elasticsearch) for complex queries
- Write events to event store, project to read models
- Reports/analytics query read models, not operational databases

---

## Technology Choices per Service

| Service | Database Technology | Rationale |
|---------|---------------------|-----------|
| **Cart Service** | Redis (primary) + SQL backup | Session data, high churn, fast reads/writes |
| **Product Catalog** | SQL Server / Azure SQL | Relational, ACID, complex queries (filters, search) |
| **Order Service** | SQL Server / Azure SQL | Transactional, append-only, historical queries |
| **Checkout Service** | Event Store (optional) | Saga state, event sourcing for audit |

### Cart Service: Redis Implementation (Future)

**Why Redis?**
- In-memory, microsecond latency
- Built-in TTL (expire abandoned carts after 24 hours)
- Horizontal scaling (Redis Cluster)

**Implementation:**
```csharp
// Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "CartService:";
});

// CartService.cs
public class CartService : ICartService
{
    private readonly IDistributedCache _cache;
    private readonly CartDbContext _dbContext;  // SQL backup for durability

    public async Task<Cart?> GetCartAsync(string customerId)
    {
        // Try Redis first
        var json = await _cache.GetStringAsync($"cart:{customerId}");
        if (json != null)
            return JsonSerializer.Deserialize<Cart>(json);
        
        // Fallback to SQL
        var cart = await _dbContext.Carts.Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);
        
        // Warm cache
        if (cart != null)
            await _cache.SetStringAsync($"cart:{customerId}", 
                JsonSerializer.Serialize(cart),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) });
        
        return cart;
    }
}
```

---

## Consequences

### Positive

1. **Service Independence**
   - Services can deploy schema changes without coordination
   - Technology flexibility (Redis for Cart, SQL for Orders)
   - Failure isolation (one database failure ≠ system failure)

2. **Scalability**
   - Scale databases independently based on load
   - Read replicas for read-heavy services (Product Catalog)
   - Sharding possible per service (not across services)

3. **Team Ownership**
   - Full-stack ownership (service + database)
   - Faster iteration (no shared DBA bottleneck)

### Negative

1. **Complexity**
   - Data consistency challenges (saga pattern, events)
   - Duplicate data (product info replicated)
   - More databases to manage (backups, upgrades, monitoring)

2. **Cost**
   - Multiple database instances (Azure SQL: $15-$500+/month each)
   - Redis cache for Cart Service: $10-$100+/month
   - Storage duplication

3. **Operational Overhead**
   - Monitor multiple databases
   - Backup and disaster recovery per database
   - Schema migrations per service

4. **Querying Limitations**
   - Cross-service joins impossible at database level
   - Reports require data aggregation via APIs or events
   - N+1 query problems if not careful

---

## Alternatives Considered

### Alternative 1: Shared Database (Current State)
- **Pros:** Simple, ACID transactions, easy queries
- **Cons:** Tight coupling, single point of failure, no service independence
- **Rejected:** Defeats purpose of microservices

### Alternative 2: Shared Database per Team
- **Pros:** Some isolation, fewer databases than per-service
- **Cons:** Team boundaries != service boundaries, still coupled
- **Rejected:** Not aligned with service autonomy principle

### Alternative 3: Event Sourcing for All Services
- **Pros:** Full audit trail, time-travel queries, event-driven
- **Cons:** Steep learning curve, overkill for simple CRUD, complex infrastructure
- **Deferred:** Consider for Checkout/Order services in future, not all services

### Alternative 4: Single Polyglot Database (CosmosDB)
- **Pros:** Single database, supports multiple APIs (SQL, MongoDB, Cassandra)
- **Cons:** Vendor lock-in, cost, learning curve, not optimal for all use cases
- **Rejected:** Prefer best-fit technology per service

---

## Migration Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Data loss during migration** | Critical | Dual-write period, validation, backups before cutover |
| **Data inconsistency (split-brain)** | High | Reconciliation scripts, monitoring, rollback plan |
| **Performance degradation** | Medium | Load testing before cutover, read replicas if needed |
| **Increased latency (API calls)** | Medium | Caching, async events for non-critical data |
| **Operational complexity** | Medium | Automation (Terraform, Helm), training, runbooks |

---

## Success Criteria

- ✅ Each service has dedicated database (or cache) by end of Phase 3
- ✅ Zero data loss during migration (validated via checksums)
- ✅ Cross-service queries work correctly (via API or events)
- ✅ Performance maintained or improved (p95 latency within 10% of baseline)
- ✅ Database backups automated per service
- ✅ Rollback tested for each migration phase

---

## References

- [Database per Service Pattern](https://microservices.io/patterns/data/database-per-service.html) - Chris Richardson
- [Saga Pattern](https://microservices.io/patterns/data/saga.html) - Distributed transactions
- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html) - Martin Fowler
- [Target-Architecture.md](./Target-Architecture.md) - Data access approach
- [Migration-Plan.md](./Migration-Plan.md) - Phased data migration

---

**Review Cycle:** After each database migration phase  
**Next Review:** After Cart Service database separation (Phase 1D)
