namespace RetailMonolith.Models
{
    public class Cart
    {
        public int Id { get; set; }

        public string CustomerId { get; set; } = "guest";

        public List<CartLine> Lines { get; set; } = new();
    }

    public class CartLine
    {
        public int Id { get; set; }
        public int CartId { get; set; }
        public Cart? Cart { get; set; }
        public string Sku { get; set; } = default!;
        public string Name { get; set; } = default!;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
    } 
}
