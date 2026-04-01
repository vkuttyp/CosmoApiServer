using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Tests.Transport;

public class Http3IntegrationTests
{
    [Fact]
    public async Task Http3_GetPing_ReturnsPong()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            RequestDelegate pipeline = ctx =>
            {
                if (ctx.Request.Path == "/ping")
                {
                    ctx.Response.WriteText("pong");
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.WriteText("Not Found");
                }
                return ValueTask.CompletedTask;
            };

            await server.StartAsync(
                port,
                pipeline,
                new ServiceCollection().BuildServiceProvider(),
                certPath: certPath,
                enableHttp3: true,
                cancellationToken: cts.Token);

            await using var connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
            {
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, port),
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost",
                    ApplicationProtocols = [new SslApplicationProtocol("h3")],
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                }
            }, cts.Token);

            _ = connection.AcceptInboundStreamAsync(cts.Token);
            _ = connection.AcceptInboundStreamAsync(cts.Token);
            _ = connection.AcceptInboundStreamAsync(cts.Token);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            var requestBytes = Http3Connection.EncodeRequestForTests(
            [
                (":method", "GET"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/ping")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("pong", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_StreamingResponse_WritesMultipleDataFrames()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            RequestDelegate pipeline = async ctx =>
            {
                if (ctx.Request.Path == "/stream")
                {
                    await ctx.Response.WriteStreamingResponseAsync(200, async body =>
                    {
                        await body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"id\":1}\n"));
                        await body.FlushAsync();
                        await body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"id\":2}\n"));
                    }, ctx.RequestAborted);
                    return;
                }

                ctx.Response.StatusCode = 404;
                ctx.Response.WriteText("Not Found");
            };

            await server.StartAsync(
                port,
                pipeline,
                new ServiceCollection().BuildServiceProvider(),
                certPath: certPath,
                enableHttp3: true,
                cancellationToken: cts.Token);

            await using var connection = await OpenConnectionAsync(port, cts.Token);
            await PrimeServerStreamsAsync(connection, cts.Token);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            var requestBytes = Http3Connection.EncodeRequestForTests(
            [
                (":method", "GET"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/stream")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, chunks) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Contains(headers, h => h.name == "content-type" && h.value == "application/x-ndjson");
            Assert.Equal("{\"id\":1}\n{\"id\":2}\n", body);
            Assert.Equal(2, chunks.Count);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_ControllerStyleStreamingWriter_WritesNdjson()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            RequestDelegate pipeline = ctx =>
            {
                if (ctx.Request.Path == "/controller-stream")
                {
                    ctx.StreamingBodyWriter = async body =>
                    {
                        await body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"id\":1}\n"));
                        await body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"id\":2}\n"));
                    };
                    return ValueTask.CompletedTask;
                }

                ctx.Response.StatusCode = 404;
                ctx.Response.WriteText("Not Found");
                return ValueTask.CompletedTask;
            };

            await server.StartAsync(
                port,
                pipeline,
                new ServiceCollection().BuildServiceProvider(),
                certPath: certPath,
                enableHttp3: true,
                cancellationToken: cts.Token);

            await using var connection = await OpenConnectionAsync(port, cts.Token);
            await PrimeServerStreamsAsync(connection, cts.Token);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            var requestBytes = Http3Connection.EncodeRequestForTests(
            [
                (":method", "GET"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/controller-stream")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (_, body, chunks) = ParseResponse(responseBytes);

            Assert.Equal("{\"id\":1}\n{\"id\":2}\n", body);
            Assert.Single(chunks);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_RequestBodyStream_ReadsAcrossMultipleDataFrames()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            RequestDelegate pipeline = async ctx =>
            {
                if (ctx.Request.Path == "/echo")
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.BodyStream.CopyToAsync(ms, ctx.RequestAborted);
                    ctx.Response.WriteText(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
                    return;
                }

                ctx.Response.StatusCode = 404;
                ctx.Response.WriteText("Not Found");
            };

            await server.StartAsync(
                port,
                pipeline,
                new ServiceCollection().BuildServiceProvider(),
                certPath: certPath,
                enableHttp3: true,
                cancellationToken: cts.Token);

            await using var connection = await OpenConnectionAsync(port, cts.Token);
            await PrimeServerStreamsAsync(connection, cts.Token);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            using var requestPayload = new MemoryStream();
            requestPayload.Write(Http3Connection.EncodeRequestForTests(
            [
                (":method", "POST"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/echo"),
                ("content-type", "text/plain"),
                ("content-length", "11")
            ]));
            WriteFrame(requestPayload, 0x00, System.Text.Encoding.UTF8.GetBytes("hello "));
            WriteFrame(requestPayload, 0x00, System.Text.Encoding.UTF8.GetBytes("world"));

            await requestStream.WriteAsync(requestPayload.ToArray(), true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("hello world", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    private static async Task<byte[]> ReadAllAsync(QuicStream stream, CancellationToken ct)
    {
#pragma warning disable CA1416
        using var ms = new MemoryStream();
        var buffer = new byte[2048];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
#pragma warning restore CA1416
    }

    private static (IReadOnlyList<(string name, string value)> headers, string body, IReadOnlyList<byte[]> dataFrames) ParseResponse(byte[] bytes)
    {
        ReadOnlySpan<byte> data = bytes;
        int pos = 0;
        IReadOnlyList<(string name, string value)> headers = [];
        var chunks = new List<byte[]>();

        while (pos < data.Length)
        {
            long type = ReadVarInt(data, ref pos);
            long length = ReadVarInt(data, ref pos);
            var payload = data.Slice(pos, (int)length);
            pos += (int)length;

            if (type == 0x01)
                headers = Http3Connection.DecodeFieldSectionForTests(payload.ToArray());
            else if (type == 0x00)
                chunks.Add(payload.ToArray());
        }

        using var body = new MemoryStream();
        foreach (var chunk in chunks)
            body.Write(chunk, 0, chunk.Length);

        return (headers, System.Text.Encoding.UTF8.GetString(body.ToArray()), chunks);
    }

    private static long ReadVarInt(ReadOnlySpan<byte> data, ref int pos)
    {
        byte first = data[pos];
        int length = 1 << (first >> 6);
        long value = first & 0x3F;
        pos++;
        for (int i = 1; i < length; i++)
            value = (value << 8) | data[pos++];
        return value;
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<QuicConnection> OpenConnectionAsync(int port, CancellationToken ct)
    {
        return await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, port),
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                ApplicationProtocols = [new SslApplicationProtocol("h3")],
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
        }, ct);
    }

    private static async Task PrimeServerStreamsAsync(QuicConnection connection, CancellationToken ct)
    {
        _ = connection.AcceptInboundStreamAsync(ct);
        _ = connection.AcceptInboundStreamAsync(ct);
        _ = connection.AcceptInboundStreamAsync(ct);
        await Task.Yield();
    }

    private static void WriteFrame(Stream stream, long type, byte[] payload)
    {
        WriteVarInt(stream, type);
        WriteVarInt(stream, payload.LongLength);
        stream.Write(payload, 0, payload.Length);
    }

    private static void WriteVarInt(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        int length = EncodeVarInt(value, buffer);
        stream.Write(buffer[..length]);
    }

    private static int EncodeVarInt(long value, Span<byte> destination)
    {
        if (value < 64)
        {
            destination[0] = (byte)value;
            return 1;
        }
        if (value < 16384)
        {
            destination[0] = (byte)(0x40 | ((value >> 8) & 0x3F));
            destination[1] = (byte)(value & 0xFF);
            return 2;
        }
        if (value < 1073741824)
        {
            destination[0] = (byte)(0x80 | ((value >> 24) & 0x3F));
            destination[1] = (byte)((value >> 16) & 0xFF);
            destination[2] = (byte)((value >> 8) & 0xFF);
            destination[3] = (byte)(value & 0xFF);
            return 4;
        }

        destination[0] = (byte)(0xC0 | ((value >> 56) & 0x3F));
        destination[1] = (byte)((value >> 48) & 0xFF);
        destination[2] = (byte)((value >> 40) & 0xFF);
        destination[3] = (byte)((value >> 32) & 0xFF);
        destination[4] = (byte)((value >> 24) & 0xFF);
        destination[5] = (byte)((value >> 16) & 0xFF);
        destination[6] = (byte)((value >> 8) & 0xFF);
        destination[7] = (byte)(value & 0xFF);
        return 8;
    }
}
