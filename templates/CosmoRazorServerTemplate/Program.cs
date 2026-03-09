using CosmoApiServer.Core.Hosting;

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(8080)
    .UseLogging()
    .UseStaticFiles()
    .AddRazorComponents();

var app = builder.Build();

app.MapGet("/health", async ctx =>
    await ctx.Response.WriteJsonAsync(new { status = "healthy", time = DateTime.UtcNow }));

Console.WriteLine("Cosmo Razor Server running on http://localhost:8080");
app.Run();
