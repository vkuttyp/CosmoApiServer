var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // silence startup noise
var app = builder.Build();

var items = Enumerable.Range(1, 20)
    .Select(i => new Item(i, $"Item {i}", i % 2 == 0 ? "Electronics" : "Books", 9.99 + i))
    .ToList();

// GET /ping → minimal JSON
app.MapGet("/ping", () => Results.Ok(new { status = "ok", server = "ASP.NET Core" }));

// GET /items → list of 20 items
app.MapGet("/items", () => Results.Ok(items));

// GET /items/{id} → single item by id
app.MapGet("/items/{id}", (int id) =>
    id >= 1 && id <= items.Count
        ? Results.Ok(items[id - 1])
        : Results.NotFound(new { error = "not found" }));

// POST /echo → deserialize and echo back
app.MapPost("/echo", async (HttpContext ctx) =>
{
    using var sr = new StreamReader(ctx.Request.Body);
    var body = await sr.ReadToEndAsync();
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync(body);
});

Console.WriteLine("AspNetBenchHost listening on http://localhost:9002");
app.Run("http://localhost:9002");

record Item(int Id, string Name, string Category, double Price);
