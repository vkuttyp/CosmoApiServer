namespace CosmoS3;

internal static class Constants
{
    internal const string AmazonTimestampFormatVerbose = "ddd, dd MMM yyy HH:mm:ss 'GMT'";
    internal const string AmazonTimestampFormatCompact = "yyyyMMddTHHmmssZ";
    internal const string AmazonDatestampFormat = "yyyyMMdd";

    internal const string HeaderStorageClass = "x-amz-storage-class";
    internal const string HeaderLastModified = "Last-Modified";
    internal const string HeaderRequestId = "x-amz-request-id";
    internal const string HeaderTraceId = "x-amz-id-2";
    internal const string HeaderBucketRegion = "x-amz-bucket-region";
    internal const string HeaderETag = "ETag";
    internal const string HeaderConnection = "Connection";
    internal const string HeaderAcceptRanges = "Accept-Ranges";

    internal const string ContentTypeXml = "application/xml";
    internal const string ContentTypeText = "text/plain";
    internal const string ContentTypeOctetStream = "application/octet-stream";

    internal const string Logo = "CosmoS3";

    internal static class Headers
    {
        internal const string DeleteMarker = "x-amz-delete-marker";
        internal const string AuthorizationHeader = "Authorization";
        internal const string ContentMd5 = "Content-MD5";
        internal const string AccessControlList = "x-amz-acl";
        internal const string AclGrantRead = "x-amz-grant-read";
        internal const string AclGrantWrite = "x-amz-grant-write";
        internal const string AclGrantReadAcp = "x-amz-grant-read-acp";
        internal const string AclGrantWriteAcp = "x-amz-grant-write-acp";
        internal const string AclGrantFullControl = "x-amz-grant-full-control";
    }
}
