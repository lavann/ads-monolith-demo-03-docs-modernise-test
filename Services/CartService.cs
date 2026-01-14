using Microsoft.EntityFrameworkCore;
using RetailMonolith.Data;
using RetailMonolith.Models;

namespace RetailMonolith.Services
{
    public class CartService : ICartService
    {
        private readonly AppDbContext _db;
        public CartService(AppDbContext db) => _db = db;

        public async Task AddToCartAsync(string customerId, int productId, int quantity = 1, CancellationToken ct = default)
        {
            //get or create cart
            var cart = await GetOrCreateCartAsync(customerId, ct);

            //get product
            var product = await _db.Products.FindAsync(new object[] { productId }, ct);
            if (product is null)
            {
                throw new InvalidOperationException("Invalid product ID");
            }

            //check if product already exists in cart
            var existing = cart.Lines.FirstOrDefault(line => line.Sku == product.Sku);

            //if exists, update quantity otherwise add new line
            if (existing is not null)
            {
                existing.Quantity += quantity;
            }
            else
            {
                var line = new CartLine
                {
                    CartId = cart.Id,
                    Sku = product.Sku,
                    Name = product.Name,
                    UnitPrice = product.Price,
                    Quantity = quantity
                };
                cart.Lines.Add(line);
            }

            await _db.SaveChangesAsync(ct);

        }

        public async Task ClearCartAsync(string customerId, CancellationToken ct = default)
        {
            var cart = await _db.Carts
                .Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);
            if (cart is null) return;

            _db.Carts.Remove(cart);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<Cart> GetCartWithLinesAsync(string customerId, CancellationToken ct = default)
        {
            //return cart if found otherwise return a new cart instance
            return await _db.Carts
                .Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct) ?? new Cart { CustomerId = customerId };
        }

        public async Task<Cart> GetOrCreateCartAsync(string customerId, CancellationToken ct = default)
        {
            //get cart
            var cart = await _db.Carts
                .Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

            //create cart if not found
            if (cart is null)
            {
                cart = new Cart { CustomerId = customerId };
                _db.Carts.Add(cart);
                await _db.SaveChangesAsync(ct);
            }
            //cart won't be null as we are creating a new instance of a cart if it is null
            return cart;
        }
    }
}
