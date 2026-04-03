using Google.Protobuf;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Grpc;

/// <summary>
/// Typed context for a gRPC unary call. Provides decoded request and helpers to write the response.
/// </summary>
public sealed class GrpcUnaryContext<TRequest, TResponse>(HttpContext httpContext)
    where TRequest  : IMessage<TRequest>, new()
    where TResponse : IMessage<TResponse>
{
    private static readonly MessageParser<TRequest> Parser = new(() => new TRequest());

    /// <summary>Decoded Protobuf request message.</summary>
    public TRequest Request { get; } = DecodeRequest(httpContext.Request.Body);

    public HttpContext HttpContext => httpContext;
    public CancellationToken CancellationToken => httpContext.RequestAborted;

    /// <summary>Write a successful unary response.</summary>
    public void WriteResponse(TResponse response)
    {
        httpContext.Response.StatusCode = 200;
        httpContext.Response.Headers["content-type"] = "application/grpc";
        httpContext.Response.Headers["grpc-encoding"] = "identity";
        httpContext.Response.Write(GrpcFraming.Encode(response));
        GrpcFraming.WriteTrailers(httpContext.Response, GrpcStatusCode.OK);
    }

    private static TRequest DecodeRequest(byte[] body)
    {
        var frame = GrpcFraming.Decode(body);
        if (frame is null) return new TRequest();
        return Parser.ParseFrom(frame.Value.payload);
    }
}

/// <summary>
/// Context for server-streaming gRPC calls.
/// </summary>
public sealed class GrpcServerStreamingContext<TRequest, TResponse>(HttpContext httpContext)
    where TRequest  : IMessage<TRequest>, new()
    where TResponse : IMessage<TResponse>
{
    private static readonly MessageParser<TRequest> Parser = new(() => new TRequest());

    public TRequest Request { get; } = DecodeRequest(httpContext.Request.Body);
    public HttpContext HttpContext => httpContext;
    public CancellationToken CancellationToken => httpContext.RequestAborted;

    /// <summary>Stream a single response message to the client.</summary>
    public void WriteMessage(TResponse message)
    {
        if (!httpContext.Response.IsStarted)
        {
            httpContext.Response.StatusCode = 200;
            httpContext.Response.Headers["content-type"] = "application/grpc";
            httpContext.Response.Headers["grpc-encoding"] = "identity";
        }
        httpContext.Response.Write(GrpcFraming.Encode(message));
    }

    /// <summary>Complete the stream with OK status (call after all WriteMessage calls).</summary>
    public void Complete(GrpcStatusCode status = GrpcStatusCode.OK, string? message = null)
        => GrpcFraming.WriteTrailers(httpContext.Response, status, message);

    private static TRequest DecodeRequest(byte[] body)
    {
        var frame = GrpcFraming.Decode(body);
        if (frame is null) return new TRequest();
        return Parser.ParseFrom(frame.Value.payload);
    }
}
