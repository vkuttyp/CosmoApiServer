using System.Text.Json.Serialization;

namespace CosmoS3.Classes
{
    public class BucketTag
    {
        public int Id { get; set; } = 0;
        public string GUID { get; set; } = Guid.NewGuid().ToString();
        public string BucketGUID { get; set; } = null;
        public string Key { get; set; } = null;
        public string Value { get; set; } = null;
        public DateTime CreatedUtc { get; set; } = DateTime.Now.ToUniversalTime();
        public BucketTag()
        {

        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="bucketGuid">Bucket GUID.</param>
        /// <param name="key">Key.</param>
        /// <param name="val">Value.</param>
        public BucketTag(string bucketGuid, string key, string val)
        {
            if (String.IsNullOrEmpty(bucketGuid)) throw new ArgumentNullException(nameof(bucketGuid));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            BucketGUID = bucketGuid;
            Key = key;
            Value = val;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="guid">GUID.</param>
        /// <param name="bucketGuid">Bucket GUID.</param>
        /// <param name="key">Key.</param>
        /// <param name="val">Value.</param>
        public BucketTag(string guid, string bucketGuid, string key, string val)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));
            if (String.IsNullOrEmpty(bucketGuid)) throw new ArgumentNullException(nameof(bucketGuid));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            GUID = guid;
            BucketGUID = bucketGuid;
            Key = key;
            Value = val;
        }

    }
}
