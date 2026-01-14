namespace RetailMonolith.Models
{
    public class Order
    {
        public int Id { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string CustomerId { get; set; } = "guest";
        public string Status { get; set; } = "Created"; // Created|Paid|Failed|Shipped
        public decimal Total { get; set; }
        public List<OrderLine> Lines { get; set; } = new();
    }

    public class OrderLine
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public Order? Order { get; set; }
        public string Sku { get; set; } = default!;
        public string Name { get; set; } = default!;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }

    }
}
