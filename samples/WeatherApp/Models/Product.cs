namespace WeatherApp.Models;

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? CategoryId { get; set; }
}

public sealed class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal DiscountedPrice { get; set; }

    public ProductDto() { }
    public ProductDto(Product p, decimal discountedPrice)
    {
        Id = p.Id;
        Name = p.Name;
        Price = p.Price;
        DiscountedPrice = discountedPrice;
    }
}
