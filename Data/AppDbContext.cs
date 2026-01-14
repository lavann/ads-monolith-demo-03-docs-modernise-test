
using Microsoft.EntityFrameworkCore;
using RetailMonolith.Models;

namespace RetailMonolith.Data
{
    public class AppDbContext:DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Product> Products => Set<Product>();
        public DbSet<InventoryItem> Inventory => Set<InventoryItem>();
        public DbSet<Cart> Carts => Set<Cart>();
        public DbSet<CartLine> CartLines => Set<CartLine>();

        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderLine> OrderLines => Set<OrderLine>();

      


        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
            b.Entity<InventoryItem>().HasIndex(i => i.Sku).IsUnique();
        }

        public static async Task SeedAsync(AppDbContext db)
        {
            if (!await db.Products.AnyAsync())
            {

                var random = new Random();
                var categories = new[] { "Apparel", "Footwear", "Accessories", "Electronics", "Home", "Beauty" };
                var currency = "GBP"; // use GBP instead of “Stirling”

                var items = Enumerable.Range(1, 50).Select(i =>
                {
                    var category = categories[random.Next(categories.Length)];
                    var price = Math.Round((decimal)(random.NextDouble() * 100 + 5), 2); // £5–£105

                    return new Product
                    {
                        Sku = $"SKU-{i:0000}",
                        Name = $"{category} Item {i}",
                        Description = $"Sample description for {category} Item {i}.",
                        Price = price,
                        Currency = currency,
                        IsActive = true,
                        Category = category
                    };
                }).ToList();


                await db.Products.AddRangeAsync(items);
                await db.Inventory.AddRangeAsync(items.Select(p => new InventoryItem { Sku = p.Sku, Quantity = random.Next(10,200) }));
                await db.SaveChangesAsync();
            }
        }


    }
}
