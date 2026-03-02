namespace WeatherApp.Models;

public sealed class Product
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public sealed class ProductDto
{
    public string Id { get; set; } = string.Empty;
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
