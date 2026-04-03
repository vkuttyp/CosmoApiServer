using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Grpc;

/// <summary>
/// Base class for gRPC services. Derive, override the RPC methods, and register
/// with app.MapGrpcService&lt;TService&gt;().
/// </summary>
public abstract class GrpcServiceBase
{
    public HttpContext HttpContext { get; internal set; } = null!;
}

/// <summary>
/// Descriptor for a single gRPC method (unary or server-streaming).
/// </summary>
public sealed class GrpcMethodDescriptor(
    string serviceName,
    string methodName,
    GrpcMethodType methodType,
    Type serviceType,
    Func<GrpcServiceBase, HttpContext, CancellationToken, Task> handler)
{
    public string ServiceName => serviceName;
    public string MethodName => methodName;
    public GrpcMethodType MethodType => methodType;
    public Type ServiceType => serviceType;
    public string Route => $"/{serviceName}/{methodName}";
    public Func<GrpcServiceBase, HttpContext, CancellationToken, Task> Handler => handler;
}

public enum GrpcMethodType { Unary, ServerStreaming, ClientStreaming, BidirectionalStreaming }

/// <summary>
/// Defines the gRPC methods exposed by a service.
/// </summary>
public interface IGrpcServiceDescriptor
{
    string ServiceName { get; }
    IReadOnlyList<GrpcMethodDescriptor> Methods { get; }
}
