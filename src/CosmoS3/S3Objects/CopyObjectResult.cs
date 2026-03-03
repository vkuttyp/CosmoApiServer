namespace CosmoS3.S3Objects
{
    using System;
    using System.Xml.Serialization;

    /// <summary>
    /// Result returned by the CopyObject (server-side copy) operation.
    /// </summary>
    [XmlRoot("CopyObjectResult")]
    public class CopyObjectResult
    {
        [XmlElement("ETag")]
        public string ETag { get; set; } = string.Empty;

        [XmlElement("LastModified")]
        public string LastModified { get; set; } = string.Empty;

        public CopyObjectResult() { }

        public CopyObjectResult(string etag, DateTime lastModified)
        {
            ETag = $"\"{etag}\"";
            LastModified = lastModified.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
    }
}
