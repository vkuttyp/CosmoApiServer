using CosmoApiServer.Core.Hosting;

var app = CosmoWebApplicationBuilder.Create()
    .ListenOn(8080)
    .UseLogging()
    .UseCors()
    .AddControllers() // Automatically scans the current assembly for Controllers
    .Build();

// Direct route example
app.MapGet("/ping", async ctx =>
{
    ctx.Response.WriteJson(new { status = "online", server = "CosmoApiServer" });
});

Console.WriteLine("Starting CosmoApiServer on port 8080...");
app.Run();
