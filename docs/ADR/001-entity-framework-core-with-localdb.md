# ADR-001: Entity Framework Core with SQL Server LocalDB

**Status:** Accepted (Implicit)

**Date:** 2025-01-14 (Documented retrospectively)

**Context:** The Retail Monolith application requires a data persistence layer to store products, inventory, shopping carts, and orders. The application is built as an ASP.NET Core 8 demo application to illustrate monolith modernization patterns.

---

## Decision

Use **Entity Framework Core 9.0.9** as the Object-Relational Mapper (ORM) with **SQL Server LocalDB** as the development database.

---

## Rationale

### Why Entity Framework Core?

1. **ASP.NET Core Integration**
   - Native support within the .NET ecosystem
   - Seamless integration with dependency injection
   - First-class tooling support (dotnet ef CLI)

2. **Code-First Development**
   - Define data models as C# classes
   - Automatic schema generation via migrations
   - Type-safe queries with LINQ

3. **Migration Management**
   - Version-controlled database schema changes
   - Repeatable deployments across environments
   - `dotnet ef migrations` tooling simplifies schema evolution

4. **Developer Productivity**
   - Reduces boilerplate data access code
   - Automatic change tracking
   - Navigation properties for relationship handling

### Why SQL Server LocalDB?

1. **Development Convenience**
   - Lightweight, file-based SQL Server instance
   - Included with Visual Studio installation
   - No separate SQL Server service required
   - Ideal for local development and demos

2. **Production Compatibility**
   - LocalDB uses SQL Server engine
   - Easy migration path to Azure SQL or full SQL Server
   - Same T-SQL dialect and features

3. **Low Friction Setup**
   - Connection string: `Server=(localdb)\\MSSQLLocalDB`
   - Database auto-created on first run
   - No installation or configuration steps for developers with Visual Studio

---

## Implementation Details

### DbContext Configuration

- **Class:** `AppDbContext` in `Data/AppDbContext.cs`
- **Entities:** Product, InventoryItem, Cart, CartLine, Order, OrderLine
- **Indexes:** Unique constraints on SKU fields
- **Connection String:** Configured in `appsettings.Development.json`

### Design-Time Factory

- **Class:** `DesignTimeDbContextFactory` in `Data/DesignTimeDbContextFactory.cs`
- **Purpose:** Enables `dotnet ef` tooling to work without running the application
- **Hardcoded Connection:** Uses LocalDB connection string directly

### Startup Behavior

- **Auto-Migration:** `await db.Database.MigrateAsync()` runs on startup
- **Auto-Seeding:** Seeds 50 sample products if database is empty
- **Justification:** Simplifies demo setup; users can run application immediately without manual database setup

### Dependency Injection

```csharp
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection") ??
                   "Server=(localdb)\\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true"));
```

- Registered as Scoped (default for DbContext)
- Fallback to hardcoded LocalDB connection if config missing

---

## Consequences

### Positive

1. **Rapid Development:** Minimal setup required; developers can clone and run immediately
2. **Type Safety:** LINQ queries are compile-time checked
3. **Tooling Support:** Visual Studio and VS Code provide IntelliSense, scaffolding, and migration tooling
4. **Portability:** Can swap to Azure SQL or other SQL Server instances by changing connection string
5. **Testability:** In-memory database provider available for testing

### Negative

1. **LocalDB Not Production-Ready:** Single-user, file-based database; must migrate to full SQL Server for production
2. **ORM Overhead:** EF Core adds abstraction layer and potential performance overhead compared to raw SQL
3. **Database Lock-In:** Tightly coupled to SQL Server (though EF Core supports other providers)
4. **Auto-Migration Risk:** Running migrations at startup could fail in production if multiple instances start simultaneously
5. **No Repository Pattern:** Services directly depend on `AppDbContext`, making testing more complex

### Operational

1. **Deployment Requirement:** Connection string must be configured for production (Azure SQL, etc.)
2. **Migration Strategy:** Must decide how to apply migrations in production (manual, CI/CD, startup)
3. **Seeding Logic:** `SeedAsync` method should be environment-aware (don't seed production)

---

## Alternatives Considered

### Alternative 1: Dapper (Micro-ORM)
- **Pros:** Lightweight, close to raw SQL, higher performance
- **Cons:** No change tracking, manual SQL writing, no automatic migrations
- **Rejected:** Increases development time; migrations more complex

### Alternative 2: In-Memory Database (No persistence)
- **Pros:** No external dependencies, instant startup
- **Cons:** Data lost on restart, unrealistic for demo of production patterns
- **Rejected:** Doesn't demonstrate real-world database interactions

### Alternative 3: SQLite
- **Pros:** Single-file database, cross-platform, no installation
- **Cons:** Different SQL dialect, less production-representative for Azure deployments
- **Rejected:** SQL Server LocalDB better matches production Azure SQL

### Alternative 4: Full SQL Server Instance
- **Pros:** Production-like environment
- **Cons:** Requires separate installation, configuration overhead
- **Rejected:** Too much friction for demo; LocalDB sufficient

---

## Notes

- **Future Consideration:** In a microservices decomposition, each service should own its database (Database-per-Service pattern)
- **Azure Deployment:** Change connection string to Azure SQL Database; LocalDB connection will not work in cloud environments
- **Security:** Use Managed Identity or Key Vault for production connection strings; avoid hardcoding credentials

---

## References

- [Entity Framework Core Documentation](https://learn.microsoft.com/en-us/ef/core/)
- [SQL Server LocalDB Overview](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb)
- `RetailMonolith.csproj` - Package references
- `Program.cs` - DbContext registration and auto-migration
- `Data/AppDbContext.cs` - DbContext implementation
