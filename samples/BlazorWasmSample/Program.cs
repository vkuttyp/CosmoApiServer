using System.Collections.Concurrent;
using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

// In-memory note store
var notes = new ConcurrentDictionary<string, Note>();

var builder = CosmoWebApplicationBuilder.Create(args);
builder.ListenOn(5050);
builder.UseLogging();
builder.UseExceptionHandler();

// Serve the published Blazor WASM output.
// Run ./run.sh to publish the client and start the server.
builder.UseBlazorWasm(
    outputPath: "blazor/wwwroot",
    configureFallback: o => o.ExcludedPrefixes = ["/api"]);

var app = builder.Build();

// ── API ──────────────────────────────────────────────────────────────────────

app.MapGet("/api/notes", ctx =>
{
    var sorted = notes.Values.OrderByDescending(n => n.CreatedAt).ToArray();
    ctx.Response.WriteJson(sorted);
    return ValueTask.CompletedTask;
});

app.MapPost("/api/notes", ctx =>
{
    var req = ctx.Request.ReadJson<CreateNoteRequest>()
        ?? throw new InvalidOperationException("Invalid request body.");

    if (string.IsNullOrWhiteSpace(req.Text))
        throw new InvalidOperationException("Note text is required.");

    var note = new Note(Guid.NewGuid().ToString("N"), req.Text.Trim(), DateTime.UtcNow);
    notes[note.Id] = note;

    ctx.Response.StatusCode = 201;
    ctx.Response.WriteJson(note);
    return ValueTask.CompletedTask;
});

app.MapDelete("/api/notes/{id}", ctx =>
{
    var id = ctx.Request.RouteValues.TryGetValue("id", out var v) ? v : null;
    if (string.IsNullOrEmpty(id))
        throw new InvalidOperationException("Note id is required.");

    notes.TryRemove(id, out _);
    ctx.Response.StatusCode = 204;
    return ValueTask.CompletedTask;
});

app.MapGet("/api/weather", ctx =>
{
    var summaries = new[] { "Freezing", "Cold", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Scorching" };
    var rng = new Random();
    var forecasts = Enumerable.Range(1, 7).Select(i =>
    {
        var c = rng.Next(-10, 40);
        return new Forecast(DateOnly.FromDateTime(DateTime.Today.AddDays(i)), c, c * 9 / 5 + 32, summaries[rng.Next(summaries.Length)]);
    }).ToArray();
    ctx.Response.WriteJson(forecasts);
    return ValueTask.CompletedTask;
});

// ── Run ───────────────────────────────────────────────────────────────────────

Console.WriteLine("BlazorWasmSample running on http://localhost:5050");
app.Run();

// ── Models ────────────────────────────────────────────────────────────────────

record Note(string Id, string Text, DateTime CreatedAt);
record CreateNoteRequest(string Text);
record Forecast(DateOnly Date, int TemperatureC, int TemperatureF, string Summary);
