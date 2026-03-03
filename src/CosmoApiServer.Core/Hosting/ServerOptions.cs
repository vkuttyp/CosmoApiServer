namespace CosmoApiServer.Core.Hosting;

public sealed class ServerOptions
{
    public int Port { get; set; } = 8080;
    public int MaxRequestBodySize { get; set; } = 1024 * 1024 * 1024; // 1 GB
}
