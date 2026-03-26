namespace CosmoBlazorSample.Services;
using CosmoBlazorSample.Models;

public class WeatherService
{
    public Task<WeatherForecast[]> GetForecastAsync()
    {
        var startDate = DateOnly.FromDateTime(DateTime.Now);
        var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };
        return Task.FromResult(Enumerable.Range(1, 10).Select(index => new WeatherForecast
        (
            startDate.AddDays(index),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        )).ToArray());
    }
}
