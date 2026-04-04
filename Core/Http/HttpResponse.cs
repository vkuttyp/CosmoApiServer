using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using CosmoApiServer.Core.Transport;

namespace CosmoApiServer.Core.Http;

public sealed class HttpResponse
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    public int StatusCode { get; set; } = 200;
    public string ReasonPhrase { get; set; } = "OK";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Trailers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HttpContext HttpContext { get; internal set; } = null!;

    /// <summary>
    /// Optional: The underlying buffer writer for the response body.
    /// If set, data is written directly to the transport instead of being buffered.
    /// </summary>
    public IBufferWriter<byte>? BodyWriter { get; set; }
    internal Func<int, Func<Stream, Task>, CancellationToken, Task>? StreamingResponseWriter { get; set; }

    private byte[]? _body;
    private bool _headersWritten;
    private bool _isChunked;
    private bool _transportHandled;

    /// <summary>
    /// Gets the buffered body. Returns empty if data was written directly to a non-test BodyWriter.
    /// </summary>
    public byte[] Body
    {
        get
        {
            if (_body != null) return _body;
            if (BodyWriter is TestBufferWriter tbw) return tbw.ToArray();
            return [];
        }
    }

    /// <summary>
    /// Ensures that HTTP headers are written to the transport.
    /// Only applicable when using BodyWriter.
    /// </summary>
    public void EnsureHeadersWritten()
    {
        if (_headersWritten || BodyWriter == null) return;
        
        // Headers only make sense to write automatically if we are on a real PipeWriter.
        // For TestBufferWriter, we don't need to write HTTP headers into the buffer.
        if (BodyWriter is System.IO.Pipelines.PipeWriter pw)
        {
            if (Headers.ContainsKey("Content-Length"))
            {
                _isChunked = false;
                Headers.Remove("Transfer-Encoding");
            }
            else
            {
                _isChunked = true;
                Headers["Transfer-Encoding"] = "chunked";
            }
            
            Http11Writer.WriteHeaders(pw, this, altSvcValue: AltSvcValue);
        }
        _headersWritten = true;
    }

    /// <summary>
    /// When non-null, every Write() call also appends to this list.
    /// Used by output caching middleware to capture the response body.
    /// </summary>
    internal List<byte>? BodyCapture { get; set; }

    public void Write(byte[] data) => Write(data.AsSpan());

    public void Write(ReadOnlySpan<byte> data)
    {
        BodyCapture?.AddRange(data.ToArray());

        if (BodyWriter != null)
        {
            EnsureHeadersWritten();
            if (_isChunked) WriteChunk(BodyWriter, data);
            else BodyWriter.Write(data);
            _hasStarted = true;
        }
        else
        {
            // Fallback to buffering
            if (_body == null) _body = data.ToArray();
            else
            {
                var newBody = new byte[_body.Length + data.Length];
                _body.CopyTo(newBody, 0);
                data.CopyTo(newBody.AsSpan(_body.Length));
                _body = newBody;
            }
            // Always update Content-Length to reflect the total accumulated body size
            Headers["Content-Length"] = _body.Length.ToString();
        }
    }

    private static void WriteChunk(IBufferWriter<byte> writer, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;
        Span<byte> hexBuf = stackalloc byte[8];
        int hexLen = FormatHex(data.Length, hexBuf);
        writer.Write(hexBuf[..hexLen]);
        writer.Write("\r\n"u8);
        writer.Write(data);
        writer.Write("\r\n"u8);
    }

    private static int FormatHex(int value, Span<byte> destination)
    {
        int pos = destination.Length;
        do
        {
            int nibble = value & 0xF;
            destination[--pos] = (byte)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
            value >>= 4;
        }
        while (value > 0);
        int length = destination.Length - pos;
        destination[pos..].CopyTo(destination);
        return length;
    }

    public void WriteText(string text, string contentType = "text/plain; charset=utf-8")
    {
        Headers["Content-Type"] = contentType;
        if (BodyWriter != null)
        {
            EnsureHeadersWritten();
            int byteCount = Encoding.UTF8.GetByteCount(text);
            if (_isChunked)
            {
                Span<byte> hexBuf = stackalloc byte[8];
                int hexLen = FormatHex(byteCount, hexBuf);
                BodyWriter.Write(hexBuf[..hexLen]);
                BodyWriter.Write("\r\n"u8);
                var dest = BodyWriter.GetSpan(byteCount);
                Encoding.UTF8.GetBytes(text, dest);
                BodyWriter.Advance(byteCount);
                BodyWriter.Write("\r\n"u8);
            }
            else
            {
                var dest = BodyWriter.GetSpan(byteCount);
                Encoding.UTF8.GetBytes(text, dest);
                BodyWriter.Advance(byteCount);
            }
            _hasStarted = true;
        }
        else
        {
            Write(Encoding.UTF8.GetBytes(text));
        }
    }

    public void WriteJson<T>(T value)
    {
        Headers["Content-Type"] = "application/json; charset=utf-8";
        if (BodyWriter != null)
        {
            EnsureHeadersWritten();
            if (_isChunked)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
                WriteChunk(BodyWriter, bytes);
            }
            else
            {
                using var writer = new Utf8JsonWriter(BodyWriter);
                JsonSerializer.Serialize(writer, value, JsonOptions);
            }
            _hasStarted = true;
        }
        else
        {
            Write(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
        }
    }

    public async Task WriteJsonAsync<T>(T value, CancellationToken ct = default)
    {
        Headers["Content-Type"] = "application/json; charset=utf-8";
        if (BodyWriter != null)
        {
            WriteJson(value); 
            await Task.CompletedTask;
        }
        else
        {
            using var ms = new MemoryStream();
            await JsonSerializer.SerializeAsync(ms, value, JsonOptions, ct);
            Write(ms.ToArray());
        }
    }

    /// <summary>
    /// Streams a file directly to the response body.
    /// </summary>
    public Task SendFileAsync(string path, CancellationToken ct = default) =>
        SendFileAsync(path, 0, null, ct);

    /// <summary>
    /// Streams a byte range from a file directly to the response body.
    /// </summary>
    public async Task SendFileAsync(string path, long offset, long? count, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(path);
        var fileLength = fileInfo.Length;
        if (offset < 0 || offset > fileLength)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var length = count ?? (fileLength - offset);
        if (length < 0 || offset + length > fileLength)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (!Headers.ContainsKey("Content-Length"))
            Headers["Content-Length"] = length.ToString();

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (offset > 0)
            fs.Seek(offset, SeekOrigin.Begin);
        
        if (BodyWriter != null)
        {
            EnsureHeadersWritten();
            
            if (_isChunked)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(8192);
                try
                {
                    long remaining = length;
                    while (remaining > 0)
                    {
                        int bytesRead = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                        if (bytesRead <= 0) break;
                        WriteChunk(BodyWriter, buffer.AsSpan(0, bytesRead));
                        remaining -= bytesRead;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            else
            {
                if (BodyWriter is System.IO.Pipelines.PipeWriter pw)
                {
                    await fs.CopyToAsync(pw.AsStream(), ct);
                }
                else
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(8192);
                    try
                    {
                        long remaining = length;
                        while (remaining > 0)
                        {
                            int bytesRead = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                            if (bytesRead <= 0) break;
                            this.Write(buffer.AsSpan(0, bytesRead));
                            remaining -= bytesRead;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
            _hasStarted = true;
        }
        else
        {
            _body = new byte[length];
            int totalRead = 0;
            while (totalRead < _body.Length)
            {
                int read = await fs.ReadAsync(_body.AsMemory(totalRead, _body.Length - totalRead), ct);
                if (read <= 0) break;
                totalRead += read;
            }
            if (totalRead != _body.Length)
                Array.Resize(ref _body, totalRead);
        }
    }

    /// <summary>
    /// Starts a chunked streaming response and executes the body writer.
    /// </summary>
    public async Task WriteStreamingResponseAsync(int statusCode, Func<Stream, Task> bodyWriter, CancellationToken ct = default)
    {
        this.StatusCode = statusCode;
        if (BodyWriter is System.IO.Pipelines.PipeWriter pw)
        {
            await Http11Writer.WriteStreamingResponseAsync(pw, statusCode, bodyWriter, ct);
            _hasStarted = true;
            _headersWritten = true; // prevents EnsureHeadersWritten from appending duplicate headers
            _transportHandled = true;
        }
        else if (StreamingResponseWriter is not null)
        {
            await StreamingResponseWriter(statusCode, bodyWriter, ct);
            _hasStarted = true;
            _headersWritten = true;
            _transportHandled = true;
        }
        else
        {
            // Fallback for non-piped writers (e.g. testing)
            using var ms = new MemoryStream();
            await bodyWriter(ms);
            Write(ms.ToArray());
        }
    }

    private bool _hasStarted;
    private bool _endCalled;
    public bool IsStarted => _hasStarted || _body is not null;
    public bool IsBuffered => _body is not null;
    internal bool IsTransportHandled => _transportHandled;

    /// <summary>
    /// Alt-Svc value injected by the transport layer so EnsureHeadersWritten can include it
    /// without the handler needing to know about HTTP/3 upgrade headers.
    /// </summary>
    internal string? AltSvcValue { get; set; }

    /// <summary>
    /// Clears the current buffered body.
    /// </summary>
    public void ClearBody()
    {
        _body = null;
    }

    /// <summary>
    /// Finishes the response, writing terminating chunk if needed.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    public void End()
    {
        if (_endCalled) return;
        _endCalled = true;
        if (_isChunked && BodyWriter != null)
        {
            BodyWriter.Write("0\r\n\r\n"u8);
        }
    }

    internal void Reset()
    {
        StatusCode = 200;
        ReasonPhrase = "OK";
        Headers.Clear();
        Trailers.Clear();
        BodyWriter = null;
        StreamingResponseWriter = null;
        _body = null;
        _headersWritten = false;
        _hasStarted = false;
        _isChunked = false;
        _transportHandled = false;
        BodyCapture = null;
        _endCalled = false;
        AltSvcValue = null;
    }

    /// <summary>
    /// A buffer writer used for testing purposes to capture streamed output.
    /// </summary>
    public sealed class TestBufferWriter : IBufferWriter<byte>
    {
        private readonly MemoryStream _ms = new();
        private byte[]? _pending;

        public void Advance(int count)
        {
            if (_pending != null && count > 0)
            {
                _ms.Write(_pending, 0, count);
                _pending = null;
            }
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            _pending = new byte[sizeHint > 0 ? sizeHint : 4096];
            return _pending;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            _pending = new byte[sizeHint > 0 ? sizeHint : 4096];
            return _pending;
        }

        public void Write(ReadOnlySpan<byte> value) => _ms.Write(value);
        public byte[] ToArray() => _ms.ToArray();
    }
}
