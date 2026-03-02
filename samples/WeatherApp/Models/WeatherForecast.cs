namespace WeatherApp.Models;

public record WeatherForecast(
    int Id,
    DateOnly Date,
    int TemperatureC,
    string Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public record CreateForecastRequest(
    DateOnly Date,
    int TemperatureC,
    string Summary);
