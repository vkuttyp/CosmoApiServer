using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using WeatherApp.Models;
using WeatherApp.Services;
using WeatherApp.Views.Weather;

namespace WeatherApp.Controllers;

[Route("weather")]
[Authorize]
public class WeatherController(IWeatherService weatherService) : ControllerBase
{
    /// GET /weather — list all forecasts (JSON)
    [HttpGet]
    public IActionResult GetAll() =>
        Ok(weatherService.GetAll());

    /// GET /weather/view — list all forecasts (HTML View)
    [HttpGet("view")]
    [AllowAnonymous] // Allow viewing without auth for easy testing
    public IActionResult GetView()
    {
        var forecasts = weatherService.GetAll();
        return View(WeatherList.Create(forecasts));
    }

    /// GET /weather/{id} — get a single forecast
    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var forecast = weatherService.GetById(id);
        return forecast is not null
            ? Ok(forecast)
            : NotFound($"Forecast with id {id} not found.");
    }

    /// POST /weather — create a new forecast
    [HttpPost]
    public IActionResult Create([FromBody] CreateForecastRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Summary))
            return BadRequest("Summary is required.");

        var created = weatherService.Create(request);
        return Created($"/weather/{created.Id}", created);
    }

    /// DELETE /weather/{id} — delete a forecast
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var deleted = weatherService.Delete(id);
        return deleted ? NoContent() : NotFound($"Forecast with id {id} not found.");
    }
}
