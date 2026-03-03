

namespace CosmoS3.Classes
{
    public class Credential
    {
        public int Id { get; set; } = 0;
        public string GUID { get; set; } = GuidSortable.NewGuid().ToString();
        public string UserGUID { get; set; } = GuidSortable.NewGuid().ToString();

        public string Description { get; set; } = null;

        public string AccessKey { get; set; } = null;

        public string SecretKey { get; set; } = null;
        public bool IsBase64 { get; set; } = false;
        public DateTime CreatedUtc { get; set; } = DateTime.Now.ToUniversalTime();
        public Credential()
        {

        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="userGuid">User GUID.</param>
        /// <param name="description">Description.</param>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="isBase64">Is base64 encoded.</param>
        public Credential(string userGuid, string description, string accessKey, string secretKey, bool isBase64)
        {
            if (String.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            if (String.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey));

            GUID = Guid.NewGuid().ToString();
            UserGUID = userGuid;
            Description = description;
            AccessKey = accessKey;
            SecretKey = secretKey;
            IsBase64 = isBase64;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="guid">GUID.</param>
        /// <param name="userGuid">User GUID.</param>
        /// <param name="description">Description.</param>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="isBase64">Is base64 encoded.</param>
        public Credential(string guid, string userGuid, string description, string accessKey, string secretKey, bool isBase64)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));
            if (String.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            if (String.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey));

            GUID = guid;
            UserGUID = userGuid;
            Description = description;
            AccessKey = accessKey;
            SecretKey = secretKey;
            IsBase64 = isBase64;
        }
         
    }
}
