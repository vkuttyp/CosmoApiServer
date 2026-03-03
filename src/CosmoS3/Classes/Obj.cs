using System.Text.Json.Serialization;

namespace CosmoS3.Classes
{
    /// <summary>
    /// Object stored in Less3.
    /// </summary>
    public class Obj
    {
        public int Id { get; set; } = 0;
        public string GUID { get; set; } = GuidSortable.NewGuid().ToString();
        public string BucketGUID { get; set; } = null;
        public string OwnerGUID { get; set; } = null;
        public string AuthorGUID { get; set; } = null;
        public string Key { get; set; } = null;
        public string ContentType { get; set; } = "application/octet-stream";
        public long ContentLength { get; set; } = 0;
        public long Version { get; set; } = 1;
        public string Etag { get; set; } = null;
        public RetentionType Retention { get; set; } = RetentionType.NONE;
        public string BlobFilename { get; set; } = null;
        public bool IsFolder { get; set; } = false;
        public bool DeleteMarker { get; set; } = false;
        public string Md5 { get; set; } = null;
        public DateTime CreatedUtc { get; set; } = DateTime.Now.ToUniversalTime();
        public DateTime LastUpdateUtc { get; set; } = DateTime.Now.ToUniversalTime();
        public DateTime LastAccessUtc { get; set; } = DateTime.Now.ToUniversalTime();
        public string Metadata { get; set; } = null;
        public DateTime? ExpirationUtc = null;
        public Obj()
        {

        }

    }
}
