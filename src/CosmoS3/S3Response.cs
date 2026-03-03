using System.Globalization;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoS3.S3Objects;

namespace CosmoS3;

/// <summary>
/// S3 response wrapper over CosmoApiServer.Core HttpResponse.
/// Provides XML/S3-aware Send methods that map to HttpResponse.Write().
/// </summary>
public class S3Response
{
    #region Public-Members

    public int StatusCode
    {
        get => _HttpResponse.StatusCode;
        set => _HttpResponse.StatusCode = value;
    }

    public string ContentType
    {
        get => _HttpResponse.Headers.TryGetValue("Content-Type", out var v) ? v : Constants.ContentTypeXml;
        set => _HttpResponse.Headers["Content-Type"] = value;
    }

    public long ContentLength { get; set; } = 0;

    /// <summary>Additional headers to set on the response (aside from Content-Type).</summary>
    public Dictionary<string, string> Headers => _HttpResponse.Headers;

    #endregion

    #region Private-Members

    private readonly HttpResponse _HttpResponse;
    private readonly S3Request _S3Request;

    #endregion

    #region Constructors

    public S3Response() 
    {
        _HttpResponse = new HttpResponse();
        _S3Request = new S3Request();
    }

    public S3Response(S3Context ctx)
    {
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));
        _HttpResponse = ctx.Http.Response;
        _S3Request = ctx.Request;
    }

    #endregion

    #region Public-Methods

    /// <summary>Send an empty response.</summary>
    public Task Send()
    {
        SetDefaultHeaders();
        _HttpResponse.Write(Array.Empty<byte>());
        return Task.CompletedTask;
    }

    /// <summary>Send a string body.</summary>
    public Task Send(string data)
    {
        SetDefaultHeaders();
        byte[] bytes = string.IsNullOrEmpty(data) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(data);
        _HttpResponse.Write(bytes);
        return Task.CompletedTask;
    }

    /// <summary>Send a byte array body.</summary>
    public Task Send(byte[] data)
    {
        SetDefaultHeaders();
        _HttpResponse.Write(data ?? Array.Empty<byte>());
        return Task.CompletedTask;
    }

    /// <summary>Send a stream body by reading it fully.</summary>
    public async Task Send(long contentLength, Stream stream)
    {
        SetDefaultHeaders();
        if (stream != null && contentLength > 0)
        {
            byte[] buf = new byte[contentLength];
            int read = await stream.ReadAsync(buf.AsMemory(0, (int)contentLength));
            _HttpResponse.Write(buf[..read]);
        }
        else
        {
            _HttpResponse.Write(Array.Empty<byte>());
        }
    }

    /// <summary>Send an S3 error response.</summary>
    public Task Send(Error error)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(SerializationHelper.SerializeXml(error));
        StatusCode = error.HttpStatusCode;
        ContentType = Constants.ContentTypeXml;
        SetDefaultHeaders();
        _HttpResponse.Write(bytes);
        return Task.CompletedTask;
    }

    /// <summary>Send an S3 error response by error code.</summary>
    public Task Send(ErrorCode error)
    {
        var errorBody = new Error(error);
        return Send(errorBody);
    }

    #endregion

    #region Private-Methods

    private void SetDefaultHeaders()
    {
        if (!Headers.ContainsKey("Server"))
            Headers["Server"] = "AmazonS3";

        Headers["Date"] = DateTime.UtcNow.ToString(Constants.AmazonTimestampFormatVerbose, CultureInfo.InvariantCulture);

        if (!Headers.ContainsKey("Content-Type"))
            Headers["Content-Type"] = Constants.ContentTypeXml;
    }

    #endregion
}
