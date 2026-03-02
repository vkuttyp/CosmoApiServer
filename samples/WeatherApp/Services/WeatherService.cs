using WeatherApp.Models;

namespace WeatherApp.Services;

public interface IWeatherService
{
    IReadOnlyList<WeatherForecast> GetAll();
    WeatherForecast? GetById(int id);
    WeatherForecast Create(CreateForecastRequest request);
    bool Delete(int id);
}

/// <summary>In-memory weather forecast store.</summary>
public sealed class WeatherService : IWeatherService
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild",
        "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    private readonly List<WeatherForecast> _forecasts;
    private int _nextId = 6;
    private readonly Lock _lock = new();

    public WeatherService()
    {
        // Seed with 5 days of data
        var rng = new Random(42);
        _forecasts = Enumerable.Range(1, 5).Select(i => new WeatherForecast(
            Id: i,
            Date: DateOnly.FromDateTime(DateTime.Today.AddDays(i)),
            TemperatureC: rng.Next(-20, 55),
            Summary: Summaries[rng.Next(Summaries.Length)]
        )).ToList();
    }

    public IReadOnlyList<WeatherForecast> GetAll()
    {
        lock (_lock) return _forecasts.ToList();
    }

    public WeatherForecast? GetById(int id)
    {
        lock (_lock) return _forecasts.FirstOrDefault(f => f.Id == id);
    }

    public WeatherForecast Create(CreateForecastRequest request)
    {
        lock (_lock)
        {
            var forecast = new WeatherForecast(_nextId++, request.Date, request.TemperatureC, request.Summary);
            _forecasts.Add(forecast);
            return forecast;
        }
    }

    public bool Delete(int id)
    {
        lock (_lock)
        {
            var forecast = _forecasts.FirstOrDefault(f => f.Id == id);
            if (forecast is null) return false;
            _forecasts.Remove(forecast);
            return true;
        }
    }
}
