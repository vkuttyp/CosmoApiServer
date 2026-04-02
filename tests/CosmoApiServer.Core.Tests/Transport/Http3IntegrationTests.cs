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
            await PrimeServerStreamsAsync(connection, cts.Token);

            await using var controlStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalStreamAsync(controlStream, 0x00, EncodeSettingsFrame((0x01, 512), (0x07, 2)), cts.Token);

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
            var (headers, body, _, _) = ParseResponse(responseBytes);

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
            await PrimeServerStreamsAsync(connection, cts.Token);

            await using var controlStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);
            await WriteUnidirectionalStreamAsync(controlStream, 0x00, EncodeSettingsFrame((0x01, 512), (0x07, 1)), cts.Token);

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

            await Assert.ThrowsAnyAsync<Exception>(async () => await ReadAllAsync(rejectedRequest, cts.Token));

            await encoderStream.WriteAsync(insertInstruction[2..], true, cts.Token);
            encoderStream.CompleteWrites();
            blockedRequest.CompleteWrites();

            var responseBytes = await ReadAllAsync(blockedRequest, cts.Token);
            var (headers, body, _, _) = ParseResponse(responseBytes);

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
            var (headers, body, _, _) = ParseResponse(responseBytes);
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

            var goAway = await ReadFrameAsync(controlStream, cts.Token);
            Assert.Equal(0x07, goAway.type);
            int pos = 0;
            Assert.Equal(4611686018427387900L, ReadVarInt(goAway.payload, ref pos));
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

            var goAway = await ReadFrameAsync(controlStream, cts.Token);
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
        ReadOnlySpan<byte> data = bytes;
        int pos = 0;
        IReadOnlyList<(string name, string value)> headers = [];
        IReadOnlyList<(string name, string value)> trailers = [];
        var chunks = new List<byte[]>();

        while (pos < data.Length)
        {
            long type = ReadVarInt(data, ref pos);
            long length = ReadVarInt(data, ref pos);
            var payload = data.Slice(pos, (int)length);
            pos += (int)length;

            if (type == 0x01)
            {
                if (headers.Count == 0)
                    headers = Http3Connection.DecodeFieldSectionForTests(payload.ToArray());
                else
                    trailers = Http3Connection.DecodeFieldSectionForTests(payload.ToArray());
            }
            else if (type == 0x00)
                chunks.Add(payload.ToArray());
        }

        using var body = new MemoryStream();
        foreach (var chunk in chunks)
            body.Write(chunk, 0, chunk.Length);

        return (headers, System.Text.Encoding.UTF8.GetString(body.ToArray()), chunks, trailers);
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
