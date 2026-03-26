using System.ComponentModel.DataAnnotations;

namespace CosmoKitchenSink.Models;

public class KitchenSinkModel
{
    [Required]
    [StringLength(10, ErrorMessage = "Name is too long.")]
    public string Name { get; set; } = "";

    [Required]
    [Range(1, 100, ErrorMessage = "Age must be between 1 and 100.")]
    public int Age { get; set; } = 18;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    public string? Description { get; set; }

    public bool Subscribe { get; set; }

    [Required]
    public string FavoriteColor { get; set; } = "Red";
}

public class WeatherForecast
{
    public DateTime Date { get; set; }
    public int TemperatureC { get; set; }
    public string? Summary { get; set; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
