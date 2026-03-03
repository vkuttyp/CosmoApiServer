using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using CosmoApiServer.Core.Http;

namespace CosmoS3;

/// <summary>
/// Parsed S3 request built from a CosmoApiServer.Core HttpRequest.
/// Ports the logic from S3ServerLibrary.S3Request without any WatsonWebserver dependency.
/// </summary>
public class S3Request
{
    #region Public-Members

    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N");

    public S3RequestStyle RequestStyle { get; private set; } = S3RequestStyle.Unknown;
    public S3RequestType RequestType { get; private set; } = S3RequestType.Unknown;

    public bool Chunked { get; set; } = false;

    public string? Region { get; set; } = null;
    public string? Hostname { get; set; } = null;
    public string? Host { get; set; } = null;
    public string? BaseDomain { get; set; } = null;
    public string? Bucket { get; set; } = null;
    public string? Key { get; set; } = null;
    public string? Prefix { get; set; } = null;
    public string? Delimiter { get; set; } = null;
    public string? Marker { get; set; } = null;

    public int MaxKeys
    {
        get => _MaxKeys;
        set => _MaxKeys = value < 1 ? throw new ArgumentOutOfRangeException(nameof(MaxKeys)) : value;
    }

    public int MaxParts
    {
        get => _MaxParts;
        set => _MaxParts = value < 1 ? throw new ArgumentOutOfRangeException(nameof(MaxParts)) : value;
    }

    public int PartNumber
    {
        get => _PartNumber;
        set => _PartNumber = value < 1 ? throw new ArgumentOutOfRangeException(nameof(PartNumber)) : value;
    }

    public int PartNumberMarker
    {
        get => _PartNumberMarker;
        set => _PartNumberMarker = value < 1 ? throw new ArgumentOutOfRangeException(nameof(PartNumberMarker)) : value;
    }

    public string? Authorization { get; set; } = null;
    public string? AccessKey { get; set; } = null;
    public string? Signature { get; set; } = null;
    public string? ContentMd5 { get; set; } = null;
    public string? ContentType { get; set; } = null;
    public string? ContentSha256 { get; set; } = null;
    public string? Date { get; set; } = null;
    public string? Expires { get; set; } = null;
    public string? ContinuationToken { get; set; } = null;
    public string? UploadId { get; set; } = null;
    public string? VersionId { get; set; } = null;

    public S3SignatureVersion SignatureVersion { get; set; } = S3SignatureVersion.Unknown;

    public long? RangeStart
    {
        get => _RangeStart;
        set => _RangeStart = value;
    }

    public long? RangeEnd
    {
        get => _RangeEnd;
        set => _RangeEnd = value;
    }

    /// <summary>True when the request uses V4 presigned URL query parameters.</summary>
    private bool IsPresignedV4 => QuerystringExists("X-Amz-Credential") && QuerystringExists("X-Amz-Signature");

    /// <summary>True when the request uses V2 presigned URL query parameters (AWSAccessKeyId + Signature + Expires).</summary>
    private bool IsPresignedV2 => QuerystringExists("awsaccesskeyid") && QuerystringExists("signature") && QuerystringExists("expires");

    /// <summary>True when the request carries presigned URL query parameters (V2 or V4).</summary>
    public bool IsPresigned => IsPresignedV4 || IsPresignedV2;

    /// <summary>True when a presigned URL has passed its expiry window.</summary>
    public bool IsExpired
    {
        get
        {
            if (!IsPresigned) return false;

            if (IsPresignedV4)
            {
                // V4: X-Amz-Date (yyyyMMddTHHmmssZ) + X-Amz-Expires (duration in seconds)
                if (string.IsNullOrEmpty(Date) || string.IsNullOrEmpty(Expires)) return false;
                if (!DateTime.TryParseExact(Date, "yyyyMMddTHHmmssZ",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal |
                        System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var requestTime))
                    return false;
                if (!int.TryParse(Expires, out int expiresSeconds)) return false;
                return DateTime.UtcNow > requestTime.AddSeconds(expiresSeconds);
            }
            else
            {
                // V2: Expires is a Unix timestamp (seconds since epoch)
                string? expiresStr = RetrieveQueryValue("expires");
                if (!long.TryParse(expiresStr, out long expiresUnixSeconds)) return false;
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresUnixSeconds;
            }
        }
    }

    public bool IsBucketRequest
    {
        get
        {
            return RequestType == S3RequestType.BucketDelete
                || RequestType == S3RequestType.BucketDeleteAcl
                || RequestType == S3RequestType.BucketDeleteTags
                || RequestType == S3RequestType.BucketDeleteWebsite
                || RequestType == S3RequestType.BucketExists
                || RequestType == S3RequestType.BucketRead
                || RequestType == S3RequestType.BucketReadAcl
                || RequestType == S3RequestType.BucketReadLocation
                || RequestType == S3RequestType.BucketReadLogging
                || RequestType == S3RequestType.BucketReadMultipartUploads
                || RequestType == S3RequestType.BucketReadTags
                || RequestType == S3RequestType.BucketReadVersioning
                || RequestType == S3RequestType.BucketReadVersions
                || RequestType == S3RequestType.BucketReadWebsite
                || RequestType == S3RequestType.BucketWrite
                || RequestType == S3RequestType.BucketWriteAcl
                || RequestType == S3RequestType.BucketWriteLogging
                || RequestType == S3RequestType.BucketWriteTags
                || RequestType == S3RequestType.BucketWriteVersioning
                || RequestType == S3RequestType.BucketWriteWebsite;
        }
    }

    public bool IsObjectRequest
    {
        get
        {
            return RequestType == S3RequestType.ObjectDelete
                || RequestType == S3RequestType.ObjectDeleteMultiple
                || RequestType == S3RequestType.ObjectDeleteTags
                || RequestType == S3RequestType.ObjectExists
                || RequestType == S3RequestType.ObjectRead
                || RequestType == S3RequestType.ObjectReadAcl
                || RequestType == S3RequestType.ObjectReadLegalHold
                || RequestType == S3RequestType.ObjectReadRange
                || RequestType == S3RequestType.ObjectReadRetention
                || RequestType == S3RequestType.ObjectReadTags
                || RequestType == S3RequestType.ObjectWrite
                || RequestType == S3RequestType.ObjectWriteAcl
                || RequestType == S3RequestType.ObjectWriteLegalHold
                || RequestType == S3RequestType.ObjectWriteRetention
                || RequestType == S3RequestType.ObjectWriteTags;
        }
    }

    public bool IsMultipartUploadRequest
    {
        get
        {
            return RequestType == S3RequestType.BucketReadMultipartUploads
                || RequestType == S3RequestType.ObjectAbortMultipartUpload
                || RequestType == S3RequestType.ObjectCompleteMultipartUpload
                || RequestType == S3RequestType.ObjectCreateMultipartUpload
                || RequestType == S3RequestType.ObjectDeleteMultiple
                || RequestType == S3RequestType.ObjectReadParts
                || RequestType == S3RequestType.ObjectUploadPart;
        }
    }

    public S3PermissionType PermissionsRequired
    {
        get
        {
            return RequestType switch
            {
                S3RequestType.BucketDelete or S3RequestType.BucketDeleteTags or S3RequestType.BucketDeleteWebsite
                or S3RequestType.BucketWrite or S3RequestType.BucketWriteLogging or S3RequestType.BucketWriteTags
                or S3RequestType.BucketWriteVersioning or S3RequestType.BucketWriteWebsite
                    => S3PermissionType.BucketWrite,

                S3RequestType.BucketExists or S3RequestType.BucketRead or S3RequestType.BucketReadLocation
                or S3RequestType.BucketReadLogging or S3RequestType.BucketReadTags or S3RequestType.BucketReadVersioning
                or S3RequestType.BucketReadVersions or S3RequestType.BucketReadWebsite
                    => S3PermissionType.BucketRead,

                S3RequestType.BucketReadAcl => S3PermissionType.BucketReadAcp,
                S3RequestType.BucketWriteAcl => S3PermissionType.BucketWriteAcp,

                S3RequestType.ObjectExists or S3RequestType.ObjectRead or S3RequestType.ObjectReadLegalHold
                or S3RequestType.ObjectReadRange or S3RequestType.ObjectReadRetention or S3RequestType.ObjectReadTags
                    => S3PermissionType.ObjectRead,

                S3RequestType.ObjectDelete or S3RequestType.ObjectDeleteMultiple or S3RequestType.ObjectDeleteTags
                or S3RequestType.ObjectWrite or S3RequestType.ObjectWriteLegalHold or S3RequestType.ObjectWriteRetention
                or S3RequestType.ObjectWriteTags
                    => S3PermissionType.BucketWrite,

                S3RequestType.ObjectReadAcl => S3PermissionType.ObjectReadAcp,
                S3RequestType.ObjectWriteAcl => S3PermissionType.ObjectWriteAcp,

                _ => S3PermissionType.NotApplicable,
            };
        }
    }

    [JsonPropertyOrder(998)]
    public List<string> SignedHeaders
    {
        get => _SignedHeaders;
        set => _SignedHeaders = value ?? new List<string>();
    }

    /// <summary>Raw request body bytes.</summary>
    [JsonIgnore]
    public byte[] Data { get; private set; } = Array.Empty<byte>();

    [JsonIgnore]
    public string DataAsString => Encoding.UTF8.GetString(Data);

    #endregion

    #region Private-Members

    private HttpRequest? _HttpRequest = null;
    private Action<string>? _Logger = null;
    private List<string> _SignedHeaders = new();

    private int _MaxKeys = 1000;
    private int _MaxParts = 1000;
    private int _PartNumber = 1;
    private int _PartNumberMarker = 1;
    private long? _RangeStart = null;
    private long? _RangeEnd = null;

    private Func<string, string>? _FindMatchingBaseDomain = null;

    #endregion

    #region Constructors-and-Factories

    public S3Request() { }

    /// <summary>
    /// Parse an S3 request from a CosmoApiServer.Core HttpRequest.
    /// </summary>
    public S3Request(HttpRequest request, Func<string, string>? baseDomainFinder = null, Action<string>? logger = null)
    {
        _HttpRequest = request ?? throw new ArgumentNullException(nameof(request));
        _Logger = logger;
        _FindMatchingBaseDomain = baseDomainFinder;
        ParseHttpRequest();
    }

    #endregion

    #region Public-Methods

    public bool HeaderExists(string key) =>
        _HttpRequest?.Headers.ContainsKey(key) == true;

    public bool QuerystringExists(string key) =>
        _HttpRequest?.Query.ContainsKey(key) == true;

    public string? RetrieveHeaderValue(string key) =>
        _HttpRequest?.Headers.TryGetValue(key, out var v) == true ? v : null;

    public string? RetrieveQueryValue(string key) =>
        _HttpRequest?.Query.TryGetValue(key, out var v) == true ? v : null;

    #endregion

    #region Private-Methods

    private void ParseHttpRequest()
    {
        if (_HttpRequest == null) return;

        Chunked = string.Equals(RetrieveHeaderValue("transfer-encoding"), "chunked", StringComparison.OrdinalIgnoreCase);
        Region = null;
        RequestType = S3RequestType.Unknown;
        RequestStyle = S3RequestStyle.Unknown;
        Bucket = null;
        Key = null;
        Authorization = null;
        AccessKey = null;
        Data = _HttpRequest.Body;

        // Extract hostname from Host header
        Host = RetrieveHeaderValue("host");
        Hostname = Host?.Split(':')[0];

        // Querystring parameters
        AccessKey = RetrieveQueryValue("awsaccesskeyid");
        ContinuationToken = RetrieveQueryValue("continuation-token");
        Delimiter = RetrieveQueryValue("delimiter");
        Expires = RetrieveQueryValue("expires");
        Marker = RetrieveQueryValue("marker");
        Prefix = RetrieveQueryValue("prefix");
        Signature = RetrieveQueryValue("signature");
        UploadId = RetrieveQueryValue("uploadid");
        VersionId = RetrieveQueryValue("versionid");

        if (QuerystringExists("max-keys") && int.TryParse(RetrieveQueryValue("max-keys"), out int mk)) MaxKeys = mk;
        if (QuerystringExists("max-parts") && int.TryParse(RetrieveQueryValue("max-parts"), out int mp)) MaxParts = mp;
        if (QuerystringExists("partnumber") && int.TryParse(RetrieveQueryValue("partnumber"), out int pn)) PartNumber = pn;
        if (QuerystringExists("part-number-marker") && int.TryParse(RetrieveQueryValue("part-number-marker"), out int pnm)) PartNumberMarker = pnm;

        // Headers
        if (HeaderExists("authorization"))
        {
            Authorization = RetrieveHeaderValue("authorization");
            ParseAuthorizationHeader();
        }

        // V4 presigned URL: credentials are in query parameters instead of Authorization header
        if (AccessKey == null && QuerystringExists("X-Amz-Credential"))
        {
            string? cred = RetrieveQueryValue("X-Amz-Credential");
            if (!string.IsNullOrEmpty(cred))
            {
                string[] parts = cred.Split('/');
                AccessKey = parts[0];
                if (parts.Length >= 3) Region = parts[2];
            }
            SignatureVersion = S3SignatureVersion.Version4;
            if (QuerystringExists("X-Amz-Signature")) Signature = RetrieveQueryValue("X-Amz-Signature");
            if (string.IsNullOrEmpty(Date) && QuerystringExists("X-Amz-Date")) Date = RetrieveQueryValue("X-Amz-Date");
            // X-Amz-Expires is in seconds (not a timestamp) for V4 presigned URLs
            if (QuerystringExists("X-Amz-Expires")) Expires = RetrieveQueryValue("X-Amz-Expires");
        }

        if (HeaderExists("range"))
        {
            var rangeVal = RetrieveHeaderValue("range");
            if (!string.IsNullOrEmpty(rangeVal))
                ParseRangeHeader(rangeVal, out _RangeStart, out _RangeEnd);
        }

        if (HeaderExists("content-md5")) ContentMd5 = RetrieveHeaderValue("content-md5");
        if (HeaderExists("content-type")) ContentType = RetrieveHeaderValue("content-type");

        if (HeaderExists("x-amz-content-sha256"))
        {
            ContentSha256 = RetrieveHeaderValue("x-amz-content-sha256");
            if (!string.IsNullOrEmpty(ContentSha256) && ContentSha256.ToLower().Contains("streaming"))
                Chunked = true;
        }

        if (HeaderExists("x-amz-date")) Date = RetrieveHeaderValue("x-amz-date");
        else if (HeaderExists("date")) Date = RetrieveHeaderValue("date");

        if (HeaderExists("x-amz-request-id")) RequestId = RetrieveHeaderValue("x-amz-request-id")!;
        if (HeaderExists("x-amz-id-2")) TraceId = RetrieveHeaderValue("x-amz-id-2")!;

        ParseHostnameAndPath();
        SetRequestType();

        // Decode aws-chunked payload so Data always contains the actual content
        if (Chunked && Data != null && Data.Length > 0)
            Data = DecodeAwsChunked(Data);
    }

    private void ParseAuthorizationHeader()
    {
        if (string.IsNullOrEmpty(Authorization)) return;
        string exceptionMsg = "Invalid authorization header format: " + Authorization;

        try
        {
            string[] valsOuter = Authorization.Split(new[] { ' ' }, 2);
            if (valsOuter == null || valsOuter.Length < 2) throw new ArgumentException(exceptionMsg);

            if (valsOuter[0].Equals("AWS"))
            {
                // Signature V2: AWS AWSAccessKeyId:Signature
                string[] valsInner = valsOuter[1].Split(':');
                if (valsInner.Length != 2) throw new ArgumentException(exceptionMsg);
                SignatureVersion = S3SignatureVersion.Version2;
                AccessKey = valsInner[0].Trim();
                Signature = valsInner[1].Trim();
            }
            else if (valsOuter[0].Equals("AWS4-HMAC-SHA256"))
            {
                // Signature V4
                SignatureVersion = S3SignatureVersion.Version4;
                string[] kvPairs = valsOuter[1].Split(',');

                foreach (string kv in kvPairs)
                {
                    string curr = kv.Trim();
                    if (curr.StartsWith("Credential="))
                    {
                        string[] credVals = curr.Replace("Credential=", "").Trim().Split('/');
                        if (credVals.Length >= 5)
                        {
                            AccessKey = credVals[0].Trim();
                            Region = credVals[2].Trim();
                        }
                    }
                    else if (curr.StartsWith("SignedHeaders="))
                    {
                        string[] signedHeaders = curr.Replace("SignedHeaders=", "").Trim().Split(';');
                        foreach (string h in signedHeaders)
                            SignedHeaders.Add(h.Trim());
                        SignedHeaders.Sort();
                    }
                    else if (curr.StartsWith("Signature="))
                    {
                        Signature = curr.Replace("Signature=", "").Trim();
                    }
                    else if (curr.StartsWith("Expires="))
                    {
                        Expires = curr.Replace("Expires=", "").Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _Logger?.Invoke("[S3Request] Failed to parse authorization header: " + ex.Message);
        }
    }

    private void ParseHostnameAndPath()
    {
        if (string.IsNullOrEmpty(_HttpRequest!.Path)) return;

        if (!string.IsNullOrEmpty(Hostname) && IsIpAddress(Hostname))
        {
            RequestStyle = S3RequestStyle.PathStyle;
        }
        else if (_FindMatchingBaseDomain == null)
        {
            RequestStyle = S3RequestStyle.PathStyle;
        }
        else
        {
            BaseDomain = _FindMatchingBaseDomain(Hostname ?? string.Empty);
            if (string.IsNullOrEmpty(BaseDomain))
            {
                RequestStyle = S3RequestStyle.PathStyle;
            }
            else
            {
                RequestStyle = S3RequestStyle.VirtualHostedStyle;
                string tempBase = BaseDomain.TrimStart('.');
                string temp = ReplaceLastOccurrence(Hostname ?? string.Empty, tempBase, "").TrimEnd('.');
                Bucket = temp;
            }
        }

        string rawUrl = _HttpRequest.Path.TrimStart('/');

        switch (RequestStyle)
        {
            case S3RequestStyle.VirtualHostedStyle:
                Key = WebUtility.UrlDecode(rawUrl);
                break;

            case S3RequestStyle.PathStyle:
            default:
                string[] parts = rawUrl.Split(new[] { '/' }, 2);
                if (parts.Length > 0) Bucket = WebUtility.UrlDecode(parts[0]);
                if (parts.Length > 1) Key = WebUtility.UrlDecode(parts[1]);
                break;
        }

        // Normalize empty strings to null
        if (string.IsNullOrEmpty(Bucket)) Bucket = null;
        if (string.IsNullOrEmpty(Key)) Key = null;
    }

    private static void ParseRangeHeader(string header, out long? start, out long? end)
    {
        start = null;
        end = null;
        if (string.IsNullOrEmpty(header)) return;
        header = header.ToLower();
        if (header.StartsWith("bytes=")) header = header.Substring(6);
        string[] vals = header.Split('-');
        if (vals.Length != 2) return;
        if (!string.IsNullOrEmpty(vals[0])) start = Convert.ToInt64(vals[0]);
        if (!string.IsNullOrEmpty(vals[1])) end = Convert.ToInt64(vals[1]);
    }

    private void SetRequestType()
    {
        if (_HttpRequest == null) return;

        switch (_HttpRequest.Method)
        {
            case CosmoApiServer.Core.Http.HttpMethod.HEAD:
                if (string.IsNullOrEmpty(Bucket) && string.IsNullOrEmpty(Key))
                    RequestType = S3RequestType.ServiceExists;
                else if (!string.IsNullOrEmpty(Bucket) && string.IsNullOrEmpty(Key))
                    RequestType = S3RequestType.BucketExists;
                else if (!string.IsNullOrEmpty(Bucket) && !string.IsNullOrEmpty(Key))
                    RequestType = S3RequestType.ObjectExists;
                break;

            case CosmoApiServer.Core.Http.HttpMethod.GET:
                if (string.IsNullOrEmpty(Bucket) && string.IsNullOrEmpty(Key))
                {
                    RequestType = S3RequestType.ListBuckets;
                }
                else if (!string.IsNullOrEmpty(Bucket) && string.IsNullOrEmpty(Key))
                {
                    if (QuerystringExists("acl")) RequestType = S3RequestType.BucketReadAcl;
                    else if (QuerystringExists("location")) RequestType = S3RequestType.BucketReadLocation;
                    else if (QuerystringExists("logging")) RequestType = S3RequestType.BucketReadLogging;
                    else if (QuerystringExists("tagging")) RequestType = S3RequestType.BucketReadTags;
                    else if (QuerystringExists("uploads")) RequestType = S3RequestType.BucketReadMultipartUploads;
                    else if (QuerystringExists("versions")) RequestType = S3RequestType.BucketReadVersions;
                    else if (QuerystringExists("versioning")) RequestType = S3RequestType.BucketReadVersioning;
                    else if (QuerystringExists("website")) RequestType = S3RequestType.BucketReadWebsite;
                    else RequestType = S3RequestType.BucketRead;
                }
                else if (!string.IsNullOrEmpty(Bucket) && !string.IsNullOrEmpty(Key))
                {
                    if (HeaderExists("range") && _RangeStart != null) RequestType = S3RequestType.ObjectReadRange;
                    else if (QuerystringExists("acl")) RequestType = S3RequestType.ObjectReadAcl;
                    else if (QuerystringExists("legal-hold")) RequestType = S3RequestType.ObjectReadLegalHold;
                    else if (QuerystringExists("uploadid")) RequestType = S3RequestType.ObjectReadParts;
                    else if (QuerystringExists("retention")) RequestType = S3RequestType.ObjectReadRetention;
                    else if (QuerystringExists("tagging")) RequestType = S3RequestType.ObjectReadTags;
                    else RequestType = S3RequestType.ObjectRead;
                }
                break;

            case CosmoApiServer.Core.Http.HttpMethod.PUT:
                if (!string.IsNullOrEmpty(Bucket) && string.IsNullOrEmpty(Key))
                {
                    if (QuerystringExists("acl")) RequestType = S3RequestType.BucketWriteAcl;
                    else if (QuerystringExists("logging")) RequestType = S3RequestType.BucketWriteLogging;
                    else if (QuerystringExists("tagging")) RequestType = S3RequestType.BucketWriteTags;
                    else if (QuerystringExists("versioning")) RequestType = S3RequestType.BucketWriteVersioning;
                    else if (QuerystringExists("website")) RequestType = S3RequestType.BucketWriteWebsite;
                    else RequestType = S3RequestType.BucketWrite;
                }
                else if (!string.IsNullOrEmpty(Bucket) && !string.IsNullOrEmpty(Key))
                {
                    if (QuerystringExists("tagging")) RequestType = S3RequestType.ObjectWriteTags;
                    else if (QuerystringExists("acl")) RequestType = S3RequestType.ObjectWriteAcl;
                    else if (QuerystringExists("legal-hold")) RequestType = S3RequestType.ObjectWriteLegalHold;
                    else if (QuerystringExists("retention")) RequestType = S3RequestType.ObjectWriteRetention;
                    else if (QuerystringExists("partnumber") && QuerystringExists("uploadid")) RequestType = S3RequestType.ObjectUploadPart;
                    else RequestType = S3RequestType.ObjectWrite;
                }
                break;

            case CosmoApiServer.Core.Http.HttpMethod.POST:
                if (!string.IsNullOrEmpty(Bucket))
                {
                    if (QuerystringExists("delete"))
                        RequestType = S3RequestType.ObjectDeleteMultiple;

                    if (!string.IsNullOrEmpty(Key))
                    {
                        if (QuerystringExists("select") && QuerystringExists("select-type")
                            && RetrieveQueryValue("select-type") == "2")
                            RequestType = S3RequestType.ObjectSelectContent;
                        if (QuerystringExists("uploadid"))
                            RequestType = S3RequestType.ObjectCompleteMultipartUpload;
                        if (QuerystringExists("uploads"))
                            RequestType = S3RequestType.ObjectCreateMultipartUpload;
                    }
                }
                break;

            case CosmoApiServer.Core.Http.HttpMethod.DELETE:
                if (!string.IsNullOrEmpty(Bucket) && string.IsNullOrEmpty(Key))
                {
                    if (QuerystringExists("acl")) RequestType = S3RequestType.BucketDeleteAcl;
                    else if (QuerystringExists("tagging")) RequestType = S3RequestType.BucketDeleteTags;
                    else if (QuerystringExists("website")) RequestType = S3RequestType.BucketDeleteWebsite;
                    else RequestType = S3RequestType.BucketDelete;
                }
                else if (!string.IsNullOrEmpty(Bucket) && !string.IsNullOrEmpty(Key))
                {
                    if (QuerystringExists("acl")) RequestType = S3RequestType.ObjectDeleteAcl;
                    else if (QuerystringExists("tagging")) RequestType = S3RequestType.ObjectDeleteTags;
                    else if (QuerystringExists("uploadid")) RequestType = S3RequestType.ObjectAbortMultipartUpload;
                    else RequestType = S3RequestType.ObjectDelete;
                }
                break;
        }
    }

    private static bool IsIpAddress(string val)
    {
        if (string.IsNullOrEmpty(val)) return false;
        if (IPAddress.TryParse(val, out _)) return true;
        string ipv4Pattern = @"^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$";
        return Regex.IsMatch(val, ipv4Pattern);
    }

    private static string ReplaceLastOccurrence(string src, string find, string replace)
    {
        int place = src.LastIndexOf(find);
        if (place == -1) return src;
        return src.Remove(place, find.Length).Insert(place, replace);
    }

    private static byte[] DecodeAwsChunked(byte[] data)
    {
        // Format: {hex-size};chunk-signature={sig}\r\n{data}\r\n ... 0;chunk-signature={sig}\r\n\r\n
        using var ms = new System.IO.MemoryStream();
        int pos = 0;
        while (pos < data.Length)
        {
            // Find end of chunk header line
            int lineEnd = -1;
            for (int i = pos; i < data.Length - 1; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n') { lineEnd = i; break; }
            }
            if (lineEnd < 0) break;

            string header = System.Text.Encoding.ASCII.GetString(data, pos, lineEnd - pos);
            int semiIdx = header.IndexOf(';');
            string hexSize = semiIdx >= 0 ? header.Substring(0, semiIdx) : header;
            if (!int.TryParse(hexSize.Trim(), System.Globalization.NumberStyles.HexNumber, null, out int chunkSize))
                break;

            pos = lineEnd + 2; // skip CRLF
            if (chunkSize == 0) break;
            if (pos + chunkSize > data.Length) chunkSize = data.Length - pos;

            ms.Write(data, pos, chunkSize);
            pos += chunkSize + 2; // skip trailing CRLF
        }
        return ms.ToArray();
    }

    #endregion
}
