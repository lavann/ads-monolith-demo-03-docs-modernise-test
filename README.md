# Retail Monolith App

A lightweight ASP.NET Core 8 Razor Pages application that simulates a retail monolith before decomposition.  
It includes product listing, shopping cart, checkout, and inventory management — built to demonstrate modernisation and refactoring patterns.

---

## Features

- ASP.NET Core 8 (Razor Pages)
- Entity Framework Core (SQL Server LocalDB)
- Dependency Injection with modular services:
  - `CartService`
  - `CheckoutService`
  - `MockPaymentGateway`
- 50 sample seeded products with inventory
- Minimal APIs for `/api/checkout` and `/api/orders/{id}`
- Health check endpoint at `/health`
- Ready for decomposition into microservices

---

## Project Setup

### 1? Clone the repository


git clone https://github.com/lavann/ads_monotlith_app.git
cd ads_monotlith_app


### 2 Clone the repository
.NET 8 SDK must be installed. Download it from the [.NET website](https://dotnet.microsoft.com/download/dotnet/8.0).	
SQL Server LocalDB (comes with Visual Studio) or any SQL Server instance
Ensure you have SQL Server LocalDB installed (comes with Visual Studio) or have access to any SQL Server instance.

### 3? Restore dependencies
dotnet restore

### 4? Update the database
This project uses Entity Framework Core with a design-time factory for migrations.


##  Database & Migrations

### Apply existing migrations
dotnet ef database update

### Create a new migration

If you modify models:
dotnet ef migrations add <MigrationName>
dotnet ef database update
EF Core uses DesignTimeDbContextFactory (Data/DesignTimeDbContextFactory.cs)
with the connection string:
Server=(localdb)\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true

### Seeding Sample Data

At startup, the app automatically runs:
await AppDbContext.SeedAsync(db);

This seeds 50 sample products with random categories, prices, and inventory.

To reseed manually
dotnet ef database drop -f
dotnet ef database update
dotnet run


## Run the application
dotnet run

| Path               | Description           |
| ------------------ | --------------------- |
| `/`                | Home Page             |
| `/Products`        | Product catalogue     |
| `/Cart`            | Shopping cart         |
| `/api/checkout`    | Checkout API          |
| `/api/orders/{id}` | Order details API     |
| `/health`          | Health check endpoint |
Access the app at `https://localhost:5001` or `http://localhost:5000`.



## Environment Variables (optional)
You can override the default connection string by setting the `ConnectionStrings__DefaultConnection` environment variable.
| Variable                               | Description                | Default          |
| -------------------------------------- | -------------------------- | ---------------- |
| `ConnectionStrings__DefaultConnection` | Database connection string | LocalDB instance |
| `ASPNETCORE_ENVIRONMENT`               | Environment mode           | `Development`    |
"# ads-monolith-demo-live" 
