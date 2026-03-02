using CosmoApiServer.Core.Hosting;

var app = CosmoWebApplicationBuilder.Create()
    .ListenOn(8080)
    .UseLogging()
    .UseCors()
    .AddControllers()          // scans this assembly for controllers
    .Build();

// Convention-based route alongside attribute-based controllers
app.MapGet("/ping", async ctx =>
{
    ctx.Response.WriteJson(new { status = "ok", server = "CosmoApiServer" });
});

app.MapGet("/echo/{message}", async ctx =>
{
    var msg = ctx.Request.RouteValues["message"];
    ctx.Response.WriteJson(new { echo = msg });
});

Console.WriteLine("Starting CosmoApiServer...");
app.Run();
