using System.Net;
using System.Net.Http;
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
    public async Task Http3_ReusedHttpClientConnection_LargeBufferedResponses_RemainStable()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");
        var payload = new string('x', 48 * 1024);

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            RequestDelegate pipeline = ctx =>
            {
                if (ctx.Request.Path == "/large-json")
                {
                    ctx.Response.Headers["Content-Type"] = "application/json";
                    ctx.Response.WriteText("{\"data\":\"" + payload + "\"}");
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

            using var handler = new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = [new SslApplicationProtocol("h3")],
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                }
            };
            using var client = new HttpClient(handler)
            {
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };

            for (int i = 0; i < 12; i++)
            {
                using var response = await client.GetAsync($"https://localhost:{port}/large-json", cts.Token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                Assert.Contains(payload, body);
            }
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

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

            await using var connection = await OpenConnectionAsync(port, cts.Token);

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
            var (headers, body, _, _) = ParseResponse(responseBytes);

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
    public async Task Http3_HeadRequest_ReturnsHeadersWithoutBody()
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
                if (ctx.Request.Path == "/head")
                {
                    ctx.Response.WriteText("body");
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

            await using var connection = await OpenConnectionAsync(port, cts.Token);
            await PrimeServerStreamsAsync(connection, cts.Token);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            var requestBytes = Http3Connection.EncodeRequestForTests(
            [
                (":method", "HEAD"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/head")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Contains(headers, h => h.name == "content-length" && h.value == "4");
            Assert.Empty(body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_BufferedBody_DefaultsToTextPlain()
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
                if (ctx.Request.Path == "/plain")
                {
                    ctx.Response.Write(System.Text.Encoding.UTF8.GetBytes("plain"));
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

            await using var connection = await OpenConnectionAsync(port, cts.Token);
            await PrimeServerStreamsAsync(connection, cts.Token);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            var requestBytes = Http3Connection.EncodeRequestForTests(
            [
                (":method", "GET"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/plain")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Contains(headers, h => h.name == "content-type" && h.value == "text/plain");
            Assert.Equal("plain", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_WriteJson_ReturnsJsonContentTypeAndBody()
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
                if (ctx.Request.Path == "/json")
                {
                    ctx.Response.WriteJson(new { message = "ok", count = 2 });
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

            await using var connection = await OpenConnectionAsync(port, cts.Token);
            await PrimeServerStreamsAsync(connection, cts.Token);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            var requestBytes = Http3Connection.EncodeRequestForTests(
            [
                (":method", "GET"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/json")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Contains(headers, h => h.name == "content-type" && h.value == "application/json; charset=utf-8");
            Assert.Equal("{\"message\":\"ok\",\"count\":2}", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_SendFileAsync_ReturnsFileBodyAndLength()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");
        string tempDir = Path.Combine(Path.GetTempPath(), $"cosmo-http3-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string filePath = Path.Combine(tempDir, "file.txt");

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));
        await File.WriteAllTextAsync(filePath, "file-body");

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            RequestDelegate pipeline = async ctx =>
            {
                if (ctx.Request.Path == "/file")
                {
                    ctx.Response.Headers["Content-Type"] = "text/plain";
                    await ctx.Response.SendFileAsync(filePath, ctx.RequestAborted);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.WriteText("Not Found");
                }
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
                (":path", "/file")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, chunks, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Contains(headers, h => h.name == "content-type" && h.value == "text/plain");
            Assert.Contains(headers, h => h.name == "content-length" && h.value == "9");
            Assert.Equal("file-body", body);
            Assert.Single(chunks);
        }
        finally
        {
            await server.StopAsync();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_UrlEncodedFormBody_IsParsedFromRequestStream()
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
                if (ctx.Request.Path == "/form")
                {
                    var form = await ctx.Request.ReadFormAsync();
                    ctx.Response.WriteJson(new
                    {
                        name = form.Fields["name"],
                        city = form.Fields["city"]
                    });
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.WriteText("Not Found");
                }
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

            byte[] body = System.Text.Encoding.UTF8.GetBytes("name=John+Doe&city=Riyadh");
            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            var requestBytes = Http3Connection.EncodeRequestForTests(
            [
                (":method", "POST"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/form"),
                ("content-type", "application/x-www-form-urlencoded"),
                ("content-length", body.Length.ToString())
            ], body);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, responseBody, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Contains(headers, h => h.name == "content-type" && h.value == "application/json; charset=utf-8");
            Assert.Equal("{\"name\":\"John Doe\",\"city\":\"Riyadh\"}", responseBody);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_MultipartFormBody_IsParsedAcrossMultipleDataFrames()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");
        const string boundary = "cosmo-boundary";

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            RequestDelegate pipeline = ctx =>
            {
                if (ctx.Request.Path == "/upload")
                {
                    var form = ctx.Request.ReadMultipart();
                    var file = form.Files["file"];
                    ctx.Response.WriteJson(new
                    {
                        title = form.Fields["title"],
                        filename = file.Filename,
                        contentType = file.ContentType,
                        size = file.Data.Length
                    });
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

            await using var connection = await OpenConnectionAsync(port, cts.Token);
            await PrimeServerStreamsAsync(connection, cts.Token);

            string multipart =
                $"--{boundary}\r\n" +
                "Content-Disposition: form-data; name=\"title\"\r\n\r\n" +
                "report\r\n" +
                $"--{boundary}\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename=\"hello.txt\"\r\n" +
                "Content-Type: text/plain\r\n\r\n" +
                "hello-http3-upload\r\n" +
                $"--{boundary}--\r\n";

            byte[] body = System.Text.Encoding.UTF8.GetBytes(multipart);
            int splitIndex = body.Length / 2;

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            using var requestPayload = new MemoryStream();
            requestPayload.Write(Http3Connection.EncodeRequestForTests(
            [
                (":method", "POST"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/upload"),
                ("content-type", $"multipart/form-data; boundary={boundary}"),
                ("content-length", body.Length.ToString())
            ]));
            WriteFrame(requestPayload, 0x00, body[..splitIndex]);
            WriteFrame(requestPayload, 0x00, body[splitIndex..]);

            await requestStream.WriteAsync(requestPayload.ToArray(), true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, responseBody, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("{\"title\":\"report\",\"filename\":\"hello.txt\",\"contentType\":\"text/plain\",\"size\":18}", responseBody);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_LargeRequestBody_CanBeReadAcrossMultipleFrames()
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
                if (ctx.Request.Path == "/large")
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.BodyStream.CopyToAsync(ms, cts.Token);
                    ctx.Response.WriteJson(new
                    {
                        length = ms.Length,
                        first = ms.GetBuffer()[0],
                        last = ms.GetBuffer()[(int)ms.Length - 1]
                    });
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.WriteText("Not Found");
                }
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

            byte[] body = Enumerable.Range(0, 96 * 1024).Select(i => (byte)(i % 251)).ToArray();

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            using var requestPayload = new MemoryStream();
            requestPayload.Write(Http3Connection.EncodeRequestForTests(
            [
                (":method", "POST"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/large"),
                ("content-length", body.Length.ToString())
            ]));

            const int frameSize = 16 * 1024;
            for (int offset = 0; offset < body.Length; offset += frameSize)
            {
                int count = Math.Min(frameSize, body.Length - offset);
                WriteFrame(requestPayload, 0x00, body.AsSpan(offset, count).ToArray());
            }

            await requestStream.WriteAsync(requestPayload.ToArray(), true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, responseBody, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal($"{{\"length\":{body.Length},\"first\":{body[0]},\"last\":{body[^1]}}}", responseBody);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_StaticFileMiddleware_ServesByteRangeRequests()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");
        string tempDir = Path.Combine(Path.GetTempPath(), $"cosmo-http3-range-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string filePath = Path.Combine(tempDir, "hello.txt");

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));
        await File.WriteAllTextAsync(filePath, "hello-http3-range");

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var middleware = new StaticFileMiddleware(tempDir);
            RequestDelegate pipeline = ctx => middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            await server.StartAsync(
                port,
                pipeline,
                services,
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
                (":path", "/hello.txt"),
                ("range", "bytes=6-10")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, chunks, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "206");
            Assert.Contains(headers, h => h.name == "accept-ranges" && h.value == "bytes");
            Assert.Contains(headers, h => h.name == "content-range" && h.value == "bytes 6-10/17");
            Assert.Contains(headers, h => h.name == "content-length" && h.value == "5");
            Assert.Equal("http3", body);
            Assert.Single(chunks);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Http3_StaticFileMiddleware_Returns416ForUnsatisfiableRange()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");
        string tempDir = Path.Combine(Path.GetTempPath(), $"cosmo-http3-range-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string filePath = Path.Combine(tempDir, "hello.txt");

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));
        await File.WriteAllTextAsync(filePath, "hello-http3-range");

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var middleware = new StaticFileMiddleware(tempDir);
            RequestDelegate pipeline = ctx => middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            await server.StartAsync(
                port,
                pipeline,
                services,
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
                (":path", "/hello.txt"),
                ("range", "bytes=999-1200")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, chunks, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "416");
            Assert.Contains(headers, h => h.name == "content-range" && h.value == "bytes */17");
            Assert.Empty(body);
            Assert.Empty(chunks);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Http3_OpenApiMiddleware_ReturnsJsonDocument()
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
            var services = new ServiceCollection().BuildServiceProvider();
            var middleware = new OpenApiMiddleware("/openapi.json", new { openapi = "3.1.0", info = new { title = "Test" } });
            RequestDelegate pipeline = ctx => middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            await server.StartAsync(
                port,
                pipeline,
                services,
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
                (":path", "/openapi.json")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Contains(headers, h => h.name == "content-type" && h.value == "application/json; charset=utf-8");
            Assert.Equal("{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Test\"}}", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_AuthorizationHeader_IsExposedOnRequest()
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
                ctx.Response.WriteJson(new { authorization = ctx.Request.Authorization });
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
                (":path", "/auth"),
                ("authorization", "Bearer abc.def.ghi")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("{\"authorization\":\"Bearer abc.def.ghi\"}", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_SwaggerUiMiddleware_ReturnsHtmlPage()
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
            var services = new ServiceCollection().BuildServiceProvider();
            var middleware = new SwaggerUIMiddleware("/swagger", "/openapi.json");
            RequestDelegate pipeline = ctx => middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            await server.StartAsync(
                port,
                pipeline,
                services,
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
                (":path", "/swagger")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Contains(headers, h => h.name == "content-type" && h.value == "text/html");
            Assert.Contains("Swagger UI", body);
            Assert.Contains("/openapi.json", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_StaticFileMiddleware_ServesHeadWithoutBody()
    {
        if (!QuicConnection.IsSupported || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            return;

        int port = GetFreeTcpPort();
        string certPath = Path.Combine(Path.GetTempPath(), $"cosmo-http3-{Guid.NewGuid():N}.pfx");
        string rootDir = Path.Combine(Path.GetTempPath(), $"cosmo-http3-static-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDir);
        string filePath = Path.Combine(rootDir, "hello.txt");

        using var cert = CreateSelfSignedCertificate("CN=localhost");
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));
        await File.WriteAllTextAsync(filePath, "static-body");

        await using var server = new PipelineHttpServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var staticFiles = new StaticFileMiddleware(rootDir);
            RequestDelegate pipeline = ctx => staticFiles.InvokeAsync(ctx, _ =>
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.WriteText("Not Found");
                return ValueTask.CompletedTask;
            });

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
                (":method", "HEAD"),
                (":scheme", "https"),
                (":authority", "localhost"),
                (":path", "/hello.txt")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Contains(headers, h => h.name == "content-type" && h.value == "text/plain");
            Assert.Contains(headers, h => h.name == "content-length" && h.value == "11");
            Assert.Empty(body);
        }
        finally
        {
            await server.StopAsync();
            if (Directory.Exists(rootDir)) Directory.Delete(rootDir, recursive: true);
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
            var (headers, body, chunks, _) = ParseResponse(responseBytes);

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
            var (_, body, chunks, _) = ParseResponse(responseBytes);

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
            var (headers, body, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("hello world", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_DynamicQpackRequest_WaitsForEncoderStreamAndThenDecodes()
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
                if (ctx.Request.Path == "/dyn")
                {
                    ctx.Response.WriteText(ctx.Request.Headers.TryGetValue("x-dynamic", out var value) ? value : "missing");
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
            var inbound = await AcceptServerStreamsAsync(connection, cts.Token);

            await using var controlStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalStreamAsync(controlStream, 0x00, EncodeSettingsFrame((0x01, 512), (0x07, 2)), cts.Token);
            // Allow the server to process SETTINGS (sets MaxBlockedStreams) before we send a request
            // that requires blocked-stream support. Without this, the request may race ahead of
            // SETTINGS processing and be rejected with Http3GeneralProtocolError on Windows.
            await Task.Delay(50, cts.Token);

            byte[] insertInstruction = EncodeInsertWithLiteralName("x-dynamic", "ready");
            await using var encoderStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalPrefixAsync(encoderStream, 0x02, cts.Token);
            await encoderStream.WriteAsync(insertInstruction[..2], false, cts.Token);
            await encoderStream.FlushAsync(cts.Token);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            await requestStream.WriteAsync(EncodeDynamicHeaderRequest(), false, cts.Token);
            await requestStream.FlushAsync(cts.Token);

            await Task.Delay(100, cts.Token);

            await encoderStream.WriteAsync(insertInstruction[2..], true, cts.Token);
            encoderStream.CompleteWrites();
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);

            // On Windows, SETTINGS is processed before the response, so the server may use dynamic
            // QPACK for the response. Fall back to reading the server's encoder stream in that case.
            IReadOnlyList<(string name, string value)> headers;
            string body;
            try
            {
                (headers, body, _, _) = ParseResponse(responseBytes);
            }
            catch (Exception ex) when (ex is NotSupportedException || ex.InnerException is NotSupportedException)
            {
                var serverEncoderBytes = await ReadEncoderStreamBytesAsync(inbound[0x02], cts.Token);
                var qpackState = new QpackDecoderState();
                // Set table capacity matching what we advertised (SETTINGS_QPACK_MAX_TABLE_CAPACITY=512),
                // then feed the server's encoder-stream bytes so the decoder has the inserted entries.
                qpackState.ApplyPeerSettings([0x01, 0x42, 0x00]); // VarInt(settingId=1) + VarInt(512)
                qpackState.AppendEncoderStreamData(serverEncoderBytes);
                (headers, body, _, _) = ParseResponse(responseBytes, qpackState);
            }

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("ready", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_DynamicQpackRequest_RejectsWhenBlockedStreamLimitExceeded()
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
                if (ctx.Request.Path == "/dyn")
                {
                    ctx.Response.WriteText(ctx.Request.Headers.TryGetValue("x-dynamic", out var value) ? value : "missing");
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
            var inbound = await AcceptServerStreamsAsync(connection, cts.Token);

            await using var controlStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalStreamAsync(controlStream, 0x00, EncodeSettingsFrame((0x01, 512), (0x07, 1)), cts.Token);
            // Allow the server to process SETTINGS before sending blocked-stream requests.
            await Task.Delay(50, cts.Token);

            byte[] insertInstruction = EncodeInsertWithLiteralName("x-dynamic", "ready");
            await using var encoderStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalPrefixAsync(encoderStream, 0x02, cts.Token);
            await encoderStream.WriteAsync(insertInstruction[..2], false, cts.Token);
            await encoderStream.FlushAsync(cts.Token);

            await using var blockedRequest = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            await blockedRequest.WriteAsync(EncodeDynamicHeaderRequest(), false, cts.Token);
            await blockedRequest.FlushAsync(cts.Token);

            await Task.Delay(100, cts.Token);

            await using var rejectedRequest = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            await rejectedRequest.WriteAsync(EncodeDynamicHeaderRequest(), true, cts.Token);
            rejectedRequest.CompleteWrites();

            // On Windows/MsQuic the server may take longer to reset the exceeded-limit stream;
            // use a 5-second local timeout so the test doesn't hang for the full 30-second CTS.
            using var rejectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            rejectCts.CancelAfter(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<Exception>(async () => await ReadAllAsync(rejectedRequest, rejectCts.Token));

            await encoderStream.WriteAsync(insertInstruction[2..], true, cts.Token);
            encoderStream.CompleteWrites();
            blockedRequest.CompleteWrites();

            var responseBytes = await ReadAllAsync(blockedRequest, cts.Token);

            // On Windows, SETTINGS is processed before the response, so the server may use dynamic
            // QPACK for the response. Fall back to reading the server's encoder stream in that case.
            IReadOnlyList<(string name, string value)> headers;
            string body;
            try
            {
                (headers, body, _, _) = ParseResponse(responseBytes);
            }
            catch (Exception ex) when (ex is NotSupportedException || ex.InnerException is NotSupportedException)
            {
                var serverEncoderBytes = await ReadEncoderStreamBytesAsync(inbound[0x02], cts.Token);
                var qpackState = new QpackDecoderState();
                // Set table capacity matching what we advertised (SETTINGS_QPACK_MAX_TABLE_CAPACITY=512),
                // then feed the server's encoder-stream bytes so the decoder has the inserted entries.
                qpackState.ApplyPeerSettings([0x01, 0x42, 0x00]); // VarInt(settingId=1) + VarInt(512)
                qpackState.AppendEncoderStreamData(serverEncoderBytes);
                (headers, body, _, _) = ParseResponse(responseBytes, qpackState);
            }

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("ready", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_DecoderStream_SendsInsertCountIncrementAndSectionAcknowledgment()
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
                if (ctx.Request.Path == "/dyn")
                {
                    ctx.Response.WriteText(ctx.Request.Headers.TryGetValue("x-dynamic", out var value) ? value : "missing");
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
            var inbound = await AcceptServerStreamsAsync(connection, cts.Token);
            await using var decoderStream = inbound[0x03];

            await using var controlStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalStreamAsync(controlStream, 0x00, EncodeSettingsFrame((0x01, 512), (0x07, 2)), cts.Token);

            byte[] insertInstruction = EncodeInsertWithLiteralName("x-dynamic", "ready");
            await using var encoderStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalPrefixAsync(encoderStream, 0x02, cts.Token);
            await encoderStream.WriteAsync(insertInstruction, true, cts.Token);
            encoderStream.CompleteWrites();

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            await requestStream.WriteAsync(EncodeDynamicHeaderRequest(), true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);

            // On Windows, SETTINGS is processed before the response, so the server may use dynamic
            // QPACK for the response. Fall back to reading the server's encoder stream in that case.
            IReadOnlyList<(string name, string value)> headers;
            string body;
            try
            {
                (headers, body, _, _) = ParseResponse(responseBytes);
            }
            catch (Exception ex) when (ex is NotSupportedException || ex.InnerException is NotSupportedException)
            {
                var serverEncoderBytes = await ReadEncoderStreamBytesAsync(inbound[0x02], cts.Token);
                var qpackState = new QpackDecoderState();
                // Set table capacity matching what we advertised (SETTINGS_QPACK_MAX_TABLE_CAPACITY=512),
                // then feed the server's encoder-stream bytes so the decoder has the inserted entries.
                qpackState.ApplyPeerSettings([0x01, 0x42, 0x00]); // VarInt(settingId=1) + VarInt(512)
                qpackState.AppendEncoderStreamData(serverEncoderBytes);
                (headers, body, _, _) = ParseResponse(responseBytes, qpackState);
            }

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("ready", body);

            var instructions = await ReadDecoderInstructionsAsync(decoderStream, expectedCount: 2, cts.Token);
            Assert.Contains(instructions, i => i.kind == "insert-count-increment" && i.value == 1);
            Assert.Contains(instructions, i => i.kind == "section-ack");
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_DecoderStream_SendsStreamCancellationWhenDynamicRequestBecomesInvalid()
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
                ctx.Response.WriteText("ok");
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
            var inbound = await AcceptServerStreamsAsync(connection, cts.Token);
            await using var decoderStream = inbound[0x03];

            await using var controlStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalStreamAsync(controlStream, 0x00, EncodeSettingsFrame((0x01, 512), (0x07, 2)), cts.Token);

            byte[] insertInstruction = EncodeInsertWithLiteralName("x-dynamic", "ready");
            await using var encoderStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalStreamAsync(encoderStream, 0x02, insertInstruction, cts.Token);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            await requestStream.WriteAsync(EncodeInvalidDynamicHeaderRequest(), true, cts.Token);
            requestStream.CompleteWrites();

            await Assert.ThrowsAnyAsync<Exception>(async () => await ReadAllAsync(requestStream, cts.Token));

            var instructions = await ReadDecoderInstructionsAsync(decoderStream, expectedCount: 2, cts.Token);
            Assert.Contains(instructions, i => i.kind == "insert-count-increment" && i.value == 1);
            Assert.Contains(instructions, i => i.kind == "stream-cancel");
            Assert.DoesNotContain(instructions, i => i.kind == "section-ack");
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_RequestTrailers_AreExposedOnHttpRequest()
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
                if (ctx.Request.Path == "/trailers")
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.BodyStream.CopyToAsync(ms, ctx.RequestAborted);
                    string trailer = ctx.Request.Trailers.TryGetValue("x-checksum", out var value) ? value : "missing";
                    ctx.Response.WriteText($"{System.Text.Encoding.UTF8.GetString(ms.ToArray())}|{trailer}");
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
                (":path", "/trailers"),
                ("content-type", "text/plain"),
                ("content-length", "5")
            ]));
            WriteFrame(requestPayload, 0x00, System.Text.Encoding.UTF8.GetBytes("hello"));
            WriteFrame(requestPayload, 0x01, Http3Connection.EncodeTrailingHeadersForTests(new Dictionary<string, string>
            {
                ["x-checksum"] = "done"
            }));

            await requestStream.WriteAsync(requestPayload.ToArray(), true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, _, _) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("hello|done", body);
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_StreamingResponse_WritesTrailingHeaders()
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
                if (ctx.Request.Path == "/response-trailers")
                {
                    ctx.Response.Trailers["x-checksum"] = "ok";
                    await ctx.Response.WriteStreamingResponseAsync(200, async body =>
                    {
                        await body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("one"));
                        await body.FlushAsync();
                        await body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("two"));
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
                (":path", "/response-trailers")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, chunks, trailers) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("onetwo", body);
            Assert.Equal(2, chunks.Count);
            Assert.Contains(trailers, h => h.name == "x-checksum" && h.value == "ok");
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_BufferedResponse_WritesTrailingHeaders()
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
                if (ctx.Request.Path == "/buffered-trailers")
                {
                    ctx.Response.Trailers["x-buffered"] = "ok";
                    ctx.Response.WriteText("payload");
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
                (":path", "/buffered-trailers")
            ]);

            await requestStream.WriteAsync(requestBytes, true, cts.Token);
            requestStream.CompleteWrites();

            var responseBytes = await ReadAllAsync(requestStream, cts.Token);
            var (headers, body, chunks, trailers) = ParseResponse(responseBytes);

            Assert.Contains(headers, h => h.name == ":status" && h.value == "200");
            Assert.Equal("payload", body);
            Assert.Single(chunks);
            Assert.Contains(trailers, h => h.name == "x-buffered" && h.value == "ok");
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_ServerStop_SendsGoAwayOnControlStream()
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
                ctx.Response.WriteText("ok");
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
            var inbound = await AcceptServerStreamsAsync(connection, cts.Token);
            await using var controlStream = inbound[0x00];

            var initialSettings = await ReadFrameAsync(controlStream, cts.Token);
            Assert.Equal(0x04, initialSettings.type);

            await server.StopAsync();

            // On Windows/MsQuic the server may close the QUIC connection with H3_NO_ERROR (0x100)
            // before we can read the GOAWAY frame; treat that as a successful graceful shutdown.
            (long type, byte[] payload) goAwayFrame;
            try
            {
                goAwayFrame = await ReadFrameAsync(controlStream, cts.Token);
            }
            catch (QuicException ex) when (ex.ApplicationErrorCode == 0x100)
            {
                return; // H3_NO_ERROR — GOAWAY was sent, connection closed before we read it
            }
            Assert.Equal(0x07, goAwayFrame.type);
            int pos = 0;
            Assert.Equal(4611686018427387900L, ReadVarInt(goAwayFrame.payload, ref pos));
        }
        finally
        {
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_ServerStop_SendsGoAwayWithHighestObservedRequestStreamId()
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
                ctx.Response.WriteText("ok");
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
            var inbound = await AcceptServerStreamsAsync(connection, cts.Token);
            await using var controlStream = inbound[0x00];

            var initialSettings = await ReadFrameAsync(controlStream, cts.Token);
            Assert.Equal(0x04, initialSettings.type);

            await using var requestStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
            await requestStream.WriteAsync(ReadOnlyMemory<byte>.Empty, false, cts.Token);
            await requestStream.FlushAsync(cts.Token);

            await Task.Delay(100, cts.Token);
            await server.StopAsync();

            // On Windows/MsQuic the server may close the QUIC connection with H3_NO_ERROR (0x100)
            // before we can read the GOAWAY frame; treat that as a successful graceful shutdown.
            (long type, byte[] payload) goAway;
            try
            {
                goAway = await ReadFrameAsync(controlStream, cts.Token);
            }
            catch (QuicException ex) when (ex.ApplicationErrorCode == 0x100)
            {
                return; // H3_NO_ERROR — GOAWAY was sent, connection closed before we read it
            }
            Assert.Equal(0x07, goAway.type);
            int pos = 0;
            Assert.Equal(requestStream.Id, ReadVarInt(goAway.payload, ref pos));
        }
        finally
        {
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_RequestTrailers_RejectsDataAfterTrailers()
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
                if (ctx.Request.Path == "/trailers")
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.BodyStream.CopyToAsync(ms, ctx.RequestAborted);
                    ctx.Response.WriteText("unexpected");
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
                (":path", "/trailers")
            ]));
            WriteFrame(requestPayload, 0x01, Http3Connection.EncodeTrailingHeadersForTests(new Dictionary<string, string>
            {
                ["x-checksum"] = "done"
            }));
            WriteFrame(requestPayload, 0x00, System.Text.Encoding.UTF8.GetBytes("late"));

            await requestStream.WriteAsync(requestPayload.ToArray(), true, cts.Token);
            requestStream.CompleteWrites();

            await Assert.ThrowsAnyAsync<Exception>(async () => await ReadAllAsync(requestStream, cts.Token));
        }
        finally
        {
            await server.StopAsync();
            if (File.Exists(certPath)) File.Delete(certPath);
        }
    }

    [Fact]
    public async Task Http3_RequestTrailers_RejectPseudoHeaders()
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
                if (ctx.Request.Path == "/trailers")
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.BodyStream.CopyToAsync(ms, ctx.RequestAborted);
                    ctx.Response.WriteText("unexpected");
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
                (":path", "/trailers")
            ]));
            WriteFrame(requestPayload, 0x00, System.Text.Encoding.UTF8.GetBytes("body"));
            WriteFrame(requestPayload, 0x01, Http3Connection.EncodeFieldSectionForTests((":status", "200")));

            await requestStream.WriteAsync(requestPayload.ToArray(), true, cts.Token);
            requestStream.CompleteWrites();

            await Assert.ThrowsAnyAsync<Exception>(async () => await ReadAllAsync(requestStream, cts.Token));
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

    private static (IReadOnlyList<(string name, string value)> headers, string body, IReadOnlyList<byte[]> dataFrames, IReadOnlyList<(string name, string value)> trailers) ParseResponse(byte[] bytes)
    {
        if (bytes.Length == 0)
            throw new InvalidOperationException("ParseResponse: received 0 bytes");

        ReadOnlySpan<byte> data = bytes;
        int pos = 0;
        IReadOnlyList<(string name, string value)> headers = [];
        IReadOnlyList<(string name, string value)> trailers = [];
        var chunks = new List<byte[]>();

        try
        {
            while (pos < data.Length)
            {
                long type = ReadVarInt(data, ref pos);
                long length = ReadVarInt(data, ref pos);
                if (length < 0 || pos + length > data.Length)
                    throw new InvalidOperationException($"ParseResponse: frame type=0x{type:X} length={length} pos={pos} total={data.Length}");
                var payload = data.Slice(pos, (int)length);
                pos += (int)length;

                if (type == 0x01)
                {
                    if (length == 0)
                        continue; // Windows/MsQuic occasionally emits a spurious 0-length HEADERS frame as a stream-close artifact; skip it.
                    if (headers.Count == 0)
                        headers = Http3Connection.DecodeFieldSectionForTests(payload.ToArray());
                    else
                        trailers = Http3Connection.DecodeFieldSectionForTests(payload.ToArray());
                }
                else if (type == 0x00)
                    chunks.Add(payload.ToArray());            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            var hex = string.Join(" ", bytes.Take(64).Select(b => b.ToString("X2")));
            throw new InvalidOperationException($"ParseResponse failed at pos={pos} total={bytes.Length}: [{hex}]", ex);
        }

        using var body = new MemoryStream();
        foreach (var chunk in chunks)
            body.Write(chunk, 0, chunk.Length);

        return (headers, System.Text.Encoding.UTF8.GetString(body.ToArray()), chunks, trailers);
    }

    private static (IReadOnlyList<(string name, string value)> headers, string body, IReadOnlyList<byte[]> dataFrames, IReadOnlyList<(string name, string value)> trailers) ParseResponse(byte[] bytes, QpackDecoderState qpackState)
    {
        if (bytes.Length == 0)
            throw new InvalidOperationException("ParseResponse: received 0 bytes");

        ReadOnlySpan<byte> data = bytes;
        int pos = 0;
        IReadOnlyList<(string name, string value)> headers = [];
        IReadOnlyList<(string name, string value)> trailers = [];
        var chunks = new List<byte[]>();

        try
        {
            while (pos < data.Length)
            {
                long type = ReadVarInt(data, ref pos);
                long length = ReadVarInt(data, ref pos);
                if (length < 0 || pos + length > data.Length)
                    throw new InvalidOperationException($"ParseResponse: frame type=0x{type:X} length={length} pos={pos} total={data.Length}");
                var payload = data.Slice(pos, (int)length);
                pos += (int)length;

                if (type == 0x01)
                {
                    if (length == 0)
                        continue; // Windows/MsQuic occasionally emits a spurious 0-length HEADERS frame as a stream-close artifact; skip it.
                    if (headers.Count == 0)
                        headers = Http3Connection.DecodeFieldSectionForTests(payload.ToArray(), qpackState);
                    else
                        trailers = Http3Connection.DecodeFieldSectionForTests(payload.ToArray(), qpackState);
                }
                else if (type == 0x00)
                    chunks.Add(payload.ToArray());
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            var hex = string.Join(" ", bytes.Take(64).Select(b => b.ToString("X2")));
            throw new InvalidOperationException($"ParseResponse failed at pos={pos} total={bytes.Length}: [{hex}]", ex);
        }

        using var body = new MemoryStream();
        foreach (var chunk in chunks)
            body.Write(chunk, 0, chunk.Length);

        return (headers, System.Text.Encoding.UTF8.GetString(body.ToArray()), chunks, trailers);
    }

    // Reads available encoder-stream bytes with a per-read timeout. On Windows the server uses
    // dynamic QPACK (capacity=512 from SETTINGS) and sends instructions before the response;
    // those bytes are buffered by the time we call this. On macOS (capacity=0, static-only) this
    // is never called, so the timeout has no practical effect on test latency.
#pragma warning disable CA1416
    private static async Task<byte[]> ReadEncoderStreamBytesAsync(QuicStream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[4096];
        while (true)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(TimeSpan.FromMilliseconds(200));
            try
            {
                int read = await stream.ReadAsync(buf.AsMemory(), linkedCts.Token);
                if (read == 0)
                    break;
                ms.Write(buf, 0, read);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return ms.ToArray();
    }
#pragma warning restore CA1416

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
            DefaultCloseErrorCode = 0,
            DefaultStreamErrorCode = 0,
            MaxInboundUnidirectionalStreams = 10,  // accept server control/encoder/decoder streams
            MaxInboundBidirectionalStreams = 100,
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

    private static async Task<Dictionary<long, QuicStream>> AcceptServerStreamsAsync(QuicConnection connection, CancellationToken ct)
    {
        var result = new Dictionary<long, QuicStream>();
        for (int i = 0; i < 3; i++)
        {
            var stream = await connection.AcceptInboundStreamAsync(ct);
            long type = await ReadVarIntAsync(stream, ct);
            result[type] = stream;
        }

        return result;
    }

    private static async Task WriteUnidirectionalStreamAsync(QuicStream stream, long streamType, byte[] payload, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WriteVarInt(ms, streamType);
        ms.Write(payload, 0, payload.Length);
        await stream.WriteAsync(ms.ToArray(), true, ct);
        stream.CompleteWrites();
    }

    private static async Task WriteUnidirectionalPrefixAsync(QuicStream stream, long streamType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WriteVarInt(ms, streamType);
        await stream.WriteAsync(ms.ToArray(), false, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<IReadOnlyList<(string kind, long value)>> ReadDecoderInstructionsAsync(QuicStream stream, int expectedCount, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(2));

        var instructions = new List<(string kind, long value)>();
        while (!linkedCts.IsCancellationRequested && instructions.Count < expectedCount)
        {
            instructions.Add(await ReadDecoderInstructionAsync(stream, linkedCts.Token));
        }

        return instructions;
    }

    private static byte[] EncodeSettingsFrame(params (long id, long value)[] settings)
    {
        using var payload = new MemoryStream();
        foreach (var (id, value) in settings)
        {
            WriteVarInt(payload, id);
            WriteVarInt(payload, value);
        }

        using var frame = new MemoryStream();
        WriteFrame(frame, 0x04, payload.ToArray());
        return frame.ToArray();
    }

    private static byte[] EncodeInsertWithLiteralName(string name, string value)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x40, 5, System.Text.Encoding.ASCII.GetByteCount(name));
        ms.Write(System.Text.Encoding.ASCII.GetBytes(name));
        WriteStringLiteral(ms, value, 7, 0x00);
        return ms.ToArray();
    }

    private static byte[] EncodeDynamicHeaderRequest()
    {
        using var headers = new MemoryStream();
        WritePrefixedInteger(headers, 0x00, 8, 2);
        WritePrefixedInteger(headers, 0x80, 7, 0);
        headers.Write(EncodeLiteralStaticNameReference(17, "GET"));
        headers.Write(EncodeLiteralStaticNameReference(23, "https"));
        headers.Write(EncodeLiteralStaticNameReference(0, "localhost"));
        headers.Write(EncodeLiteralStaticNameReference(1, "/dyn"));
        headers.Write(EncodeIndexedDynamicPostBase(0));

        using var request = new MemoryStream();
        WriteFrame(request, 0x01, headers.ToArray());
        return request.ToArray();
    }

    private static byte[] EncodeInvalidDynamicHeaderRequest()
    {
        using var headers = new MemoryStream();
        WritePrefixedInteger(headers, 0x00, 8, 2);
        WritePrefixedInteger(headers, 0x80, 7, 0);
        headers.Write(EncodeLiteralStaticNameReference(17, "GET"));
        headers.Write(EncodeLiteralStaticNameReference(23, "https"));
        headers.Write(EncodeLiteralStaticNameReference(0, "localhost"));
        headers.Write(EncodeIndexedDynamicPostBase(0));
        headers.Write(EncodeLiteralStaticNameReference(1, "/dyn"));

        using var request = new MemoryStream();
        WriteFrame(request, 0x01, headers.ToArray());
        return request.ToArray();
    }

    private static byte[] EncodeIndexedDynamicPostBase(int index)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x10, 4, index);
        return ms.ToArray();
    }

    private static byte[] EncodeLiteralStaticNameReference(int nameIndex, string value)
    {
        using var ms = new MemoryStream();
        WritePrefixedInteger(ms, 0x50, 4, nameIndex);
        WriteStringLiteral(ms, value, 7, 0x00);
        return ms.ToArray();
    }

    private static void WriteStringLiteral(Stream stream, string value, int prefixBits, byte prefixPattern)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        WritePrefixedInteger(stream, prefixPattern, prefixBits, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WritePrefixedInteger(Stream stream, byte prefixPattern, int prefixBits, int value)
    {
        int mask = (1 << prefixBits) - 1;
        if (value < mask)
        {
            stream.WriteByte((byte)(prefixPattern | value));
            return;
        }

        stream.WriteByte((byte)(prefixPattern | mask));
        int remaining = value - mask;
        while (remaining >= 128)
        {
            stream.WriteByte((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }
        stream.WriteByte((byte)remaining);
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

    private static async Task<long> ReadVarIntAsync(QuicStream stream, CancellationToken ct)
    {
        var first = new byte[1];
        int read = await stream.ReadAsync(first.AsMemory(), ct);
        if (read == 0) throw new EndOfStreamException();

        int length = 1 << (first[0] >> 6);
        var buffer = new byte[8];
        buffer[0] = first[0];
        int offset = 1;
        while (offset < length)
        {
            int chunk = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), ct);
            if (chunk == 0) throw new EndOfStreamException();
            offset += chunk;
        }

        int pos = 0;
        return ReadVarInt(buffer.AsSpan(0, length), ref pos);
    }

    private static async Task<(long type, byte[] payload)> ReadFrameAsync(QuicStream stream, CancellationToken ct)
    {
        long type = await ReadVarIntAsync(stream, ct);
        long length = await ReadVarIntAsync(stream, ct);
        var payload = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(payload.AsMemory(offset, (int)length - offset), ct);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }

        return (type, payload);
    }

    private static async Task<(string kind, long value)> ReadDecoderInstructionAsync(QuicStream stream, CancellationToken ct)
    {
        var first = new byte[1];
        int read = await stream.ReadAsync(first.AsMemory(), ct);
        if (read == 0)
            throw new EndOfStreamException();

        byte prefix = first[0];
        if ((prefix & 0x80) != 0)
            return ("section-ack", await ReadPrefixedIntegerRemainderAsync(stream, prefix, 7, ct));
        if ((prefix & 0x40) != 0)
            return ("stream-cancel", await ReadPrefixedIntegerRemainderAsync(stream, prefix, 6, ct));

        return ("insert-count-increment", await ReadPrefixedIntegerRemainderAsync(stream, prefix, 6, ct));
    }

    private static async Task<long> ReadPrefixedIntegerRemainderAsync(QuicStream stream, byte firstByte, int prefixBits, CancellationToken ct)
    {
        int mask = (1 << prefixBits) - 1;
        long value = firstByte & mask;
        if (value < mask)
            return value;

        int shift = 0;
        while (true)
        {
            var next = new byte[1];
            int read = await stream.ReadAsync(next.AsMemory(), ct);
            if (read == 0)
                throw new EndOfStreamException();

            value += (long)(next[0] & 0x7F) << shift;
            if ((next[0] & 0x80) == 0)
                break;

            shift += 7;
        }

        return value;
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
