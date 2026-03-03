

namespace CosmoS3.Classes
{
    public class ObjectTag
    {
        public int Id { get; set; } = 0;
        public string GUID { get; set; } = Guid.NewGuid().ToString();
        public string BucketGUID { get; set; } = Guid.NewGuid().ToString();
        public string ObjectGUID { get; set; } = Guid.NewGuid().ToString();
        public string Key { get; set; } = null;
        public string Value { get; set; } = null;
        public DateTime CreatedUtc { get; set; } = DateTime.Now.ToUniversalTime();
        public ObjectTag()
        {

        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="bucketGuid">Bucket GUID.</param>
        /// <param name="objectGuid">Object GUID.</param>
        /// <param name="key">Key.</param>
        /// <param name="val">Value.</param>
        public ObjectTag(string bucketGuid, string objectGuid, string key, string val)
        {
            if (String.IsNullOrEmpty(bucketGuid)) throw new ArgumentNullException(nameof(bucketGuid));
            if (String.IsNullOrEmpty(objectGuid)) throw new ArgumentNullException(nameof(objectGuid));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            BucketGUID = bucketGuid;
            ObjectGUID = objectGuid;
            Key = key;
            Value = val;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="guid">GUID.</param>
        /// <param name="bucketGuid">Bucket GUID.</param>
        /// <param name="objectGuid">Object GUID.</param>
        /// <param name="key">Key.</param>
        /// <param name="val">Value.</param>
        public ObjectTag(string guid, string bucketGuid, string objectGuid, string key, string val)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));
            if (String.IsNullOrEmpty(bucketGuid)) throw new ArgumentNullException(nameof(bucketGuid));
            if (String.IsNullOrEmpty(objectGuid)) throw new ArgumentNullException(nameof(objectGuid));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            GUID = guid;
            BucketGUID = bucketGuid;
            ObjectGUID = objectGuid;
            Key = key;
            Value = val;
        }
    }
}
