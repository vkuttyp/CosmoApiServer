using CosmoApiServer.Core.Hosting;

var items = Enumerable.Range(1, 20)
    .Select(i => new Item(i, $"Item {i}", i % 2 == 0 ? "Electronics" : "Books", 9.99 + i))
    .ToList();

var app = CosmoWebApplicationBuilder.Create()
    .ListenOn(9001)
    .Build();

// GET /ping → minimal JSON
app.MapGet("/ping", async ctx =>
    ctx.Response.WriteJson(new { status = "ok", server = "CosmoApiServer" }));

// GET /items → list of 20 items
app.MapGet("/items", async ctx =>
    ctx.Response.WriteJson(items));

// GET /items/{id} → single item by id
app.MapGet("/items/{id}", async ctx =>
{
    if (!int.TryParse(ctx.Request.RouteValues["id"]?.ToString(), out var id) ||
        id < 1 || id > items.Count)
    {
        ctx.Response.StatusCode = 404;
        ctx.Response.WriteJson(new { error = "not found" });
        return;
    }
    ctx.Response.WriteJson(items[id - 1]);
});

// POST /echo → deserialize and echo back
app.MapPost("/echo", async ctx =>
{
    var body = System.Text.Encoding.UTF8.GetString(ctx.Request.Body);
    ctx.Response.WriteText(body, "application/json; charset=utf-8");
});

Console.WriteLine("CosmoApiBenchHost listening on http://localhost:9001");
app.Run();

record Item(int Id, string Name, string Category, double Price);
