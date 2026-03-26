using CosmoKitchenSink.Models;

namespace CosmoKitchenSink.Services;

public class DataService
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    public ValueTask<WeatherForecast[]> GetForecastAsync(DateTime startDate)
    {
        var rng = new Random();
        return ValueTask.FromResult(Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = startDate.AddDays(index),
            TemperatureC = rng.Next(-20, 55),
            Summary = Summaries[rng.Next(Summaries.Length)]
        }).ToArray());
    }

    public ValueTask<string> GetUserDisplayNameAsync(int id)
    {
        return id switch
        {
            1 => ValueTask.FromResult("John Doe"),
            2 => ValueTask.FromResult("Jane Smith"),
            _ => ValueTask.FromResult("Unknown User")
        };
    }
}
