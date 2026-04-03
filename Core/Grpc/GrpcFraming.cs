using System.Buffers.Binary;
using Google.Protobuf;

namespace CosmoApiServer.Core.Grpc;

/// <summary>
/// gRPC message framing: 5-byte header (1 compression flag + 4 length) followed by Protobuf payload.
/// </summary>
public static class GrpcFraming
{
    /// <summary>Decode the first gRPC message from a byte array. Returns null if data is insufficient.</summary>
    public static (bool compressed, byte[] payload)? Decode(byte[] data)
    {
        if (data.Length < 5) return null;
        bool compressed = data[0] == 1;
        int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(1, 4));
        if (data.Length < 5 + length) return null;
        return (compressed, data[5..(5 + length)]);
    }

    /// <summary>Encode a Protobuf message with the gRPC 5-byte frame header.</summary>
    public static byte[] Encode(IMessage message)
    {
        var payload = message.ToByteArray();
        var frame = new byte[5 + payload.Length];
        frame[0] = 0; // not compressed
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), payload.Length);
        payload.CopyTo(frame, 5);
        return frame;
    }

    /// <summary>Encode raw bytes with the gRPC 5-byte frame header.</summary>
    public static byte[] Encode(byte[] payload)
    {
        var frame = new byte[5 + payload.Length];
        frame[0] = 0;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), payload.Length);
        payload.CopyTo(frame, 5);
        return frame;
    }

    /// <summary>Write gRPC trailing status headers (grpc-status, grpc-message).</summary>
    public static void WriteTrailers(CosmoApiServer.Core.Http.HttpResponse response, GrpcStatusCode status, string? message = null)
    {
        response.Trailers["grpc-status"] = ((int)status).ToString();
        if (!string.IsNullOrEmpty(message))
            response.Trailers["grpc-message"] = Uri.EscapeDataString(message);
    }
}

public enum GrpcStatusCode
{
    OK                  = 0,
    Cancelled           = 1,
    Unknown             = 2,
    InvalidArgument     = 3,
    DeadlineExceeded    = 4,
    NotFound            = 5,
    AlreadyExists       = 6,
    PermissionDenied    = 7,
    ResourceExhausted   = 8,
    FailedPrecondition  = 9,
    Aborted             = 10,
    OutOfRange          = 11,
    Unimplemented       = 12,
    Internal            = 13,
    Unavailable         = 14,
    DataLoss            = 15,
    Unauthenticated     = 16
}
