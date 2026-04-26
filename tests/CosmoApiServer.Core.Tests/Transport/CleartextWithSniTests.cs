using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Tests.Transport;

/// <summary>
/// Regression for the bug where PipelineHttpServer.StartAsync was reusing
/// the local `cert` variable when resolving a cert for QUIC. When only
/// an SNI context selector was configured (no static certPath), the
/// resolution flipped `cert` to non-null, which caused the cleartext
/// listener gate `useTls: cert is not null` to enable TLS on port 80.
/// HTTP/1.1 clients then saw silent "Empty reply from server" because
/// the server was waiting for a TLS ClientHello.
/// </summary>
public class CleartextWithSniTests
{
    [Fact]
    public async Task CleartextListener_StaysCleartext_When_OnlySniContextSelectorAndHttp3AreConfigured()
    {
        int httpPort = GetFreeTcpPort();
        int httpsPort = GetFreeTcpPort();

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        var ctx = SslStreamCertificateContext.Create(cert, additionalCertificates: null);

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        RequestDelegate pipeline = c =>
        {
            c.Response.WriteText("hello");
            return ValueTask.CompletedTask;
        };

        await server.StartAsync(
            httpPort,
            pipeline,
            new ServiceCollection().BuildServiceProvider(),
            certPath: null,                              // <-- the trigger: no static cert
            certificateContextSelector: _ => ctx,        // <-- only SNI context selector
            httpsPort: httpsPort,
            enableHttp3: true,                           // <-- forces the QUIC cert resolution branch
            cancellationToken: cts.Token);

        // Plain HTTP/1.1 GET on the cleartext port — must succeed without TLS handshake.
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, httpPort, cts.Token);
        await using var stream = client.GetStream();

        byte[] request = Encoding.ASCII.GetBytes(
            "GET /ping HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(request, cts.Token);
        await stream.FlushAsync(cts.Token);

        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        string? statusLine = await reader.ReadLineAsync(cts.Token);

        Assert.NotNull(statusLine);
        Assert.StartsWith("HTTP/1.1 200", statusLine);
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        req.CertificateExtensions.Add(san.Build());

        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
