using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Middleware;
using CosmoS3.Logging;
using CosmoS3.Settings;

namespace CosmoS3;

/// <summary>
/// Convenience factory that creates a fully configured <see cref="CosmoWebApplication"/>
/// from a <see cref="SettingsBase"/> instance.
/// 
/// Automatically applies:
/// <list type="bullet">
///   <item>HTTPS / TLS (when <see cref="SettingsBase.CertificatePath"/> is set)</item>
///   <item>HTTP/2 cleartext (when <see cref="SettingsBase.EnableHttp2"/> is <c>true</c>)</item>
///   <item>CORS middleware (when <see cref="CorsSettings.Enabled"/> is <c>true</c>)</item>
///   <item>Request logging</item>
///   <item><see cref="S3Middleware"/></item>
/// </list>
/// </summary>
public static class CosmoS3Application
{
    /// <summary>
    /// Build a CosmoApiServer application configured for S3 hosting.
    /// </summary>
    /// <param name="settings">CosmoS3 settings (storage, database, TLS, CORS, etc.).</param>
    /// <param name="port">TCP port to listen on (default: 8100).</param>
    /// <param name="logLevel">Minimum log level for S3 middleware (default: Info).</param>
    /// <returns>A built <see cref="CosmoWebApplication"/> ready for <c>.Run()</c>.</returns>
    public static CosmoWebApplication Create(
        SettingsBase settings,
        int port = 8100,
        LogLevel logLevel = LogLevel.Info)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var builder = CosmoWebApplicationBuilder.Create().ListenOn(port);

        // TLS / HTTPS
        if (settings.EnableTls)
            builder.UseHttps(settings.CertificatePath, settings.CertificatePassword);

        // HTTP/2 cleartext (h2c)
        if (settings.EnableHttp2)
            builder.UseHttp2();

        // CORS
        if (settings.Cors.Enabled)
        {
            builder.UseCors(o =>
            {
                o.AllowedOrigins = settings.Cors.AllowedOrigins;
                o.AllowedMethods = settings.Cors.AllowedMethods;
                o.AllowedHeaders = settings.Cors.AllowedHeaders;
            });
        }

        // Request logging + S3 middleware
        builder.UseLogging()
               .UseMiddleware(new S3Middleware(settings, logLevel));

        return builder.Build();
    }
}
