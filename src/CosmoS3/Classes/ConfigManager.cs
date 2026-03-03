

using CosmoS3.Settings;

using CosmoS3.Logging;
namespace CosmoS3.Classes
{
    /// <summary>
    /// Configuration manager.
    /// </summary>
    internal class ConfigManager
    {

        private SettingsBase _Settings = null;
        private S3Logger _Logging = null;
        internal ConfigManager(SettingsBase settings, S3Logger logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }


        internal List<User> GetUsers()
        {
           return DataAccess.GetUsers();
        }

        internal bool UserGuidExists(string guid)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));

           return DataAccess.UserGuidExists(guid);
        }

        internal bool UserEmailExists(string email)
        {
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));

           return DataAccess.UserEmailExists(email);
        }

        internal User GetUserByGuid(string guid)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));

            // Check in-memory users first
            var inMem = _Settings.Users.FirstOrDefault(u => u.GUID == guid);
            if (inMem != null) return inMem;

            return DataAccess.GetUserByGuid(guid);
        }

        internal User GetUserByName(string name)
        { 
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            return DataAccess.GetUserByName(name);
        }

        internal User GetUserByEmail(string email)
        { 
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));

            return DataAccess.GetUserByEmail(email);
        }

        internal User GetUserByAccessKey(string accessKey)
        { 
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));

            Credential cred = GetCredentialByAccessKey(accessKey);
            if (cred == null)
            {
                _Logging.Warn("ConfigManager GetUserByAccessKey access key " + accessKey + " not found");
                return null;
            }

            User user = GetUserByGuid(cred.UserGUID);
            if (user == null)
            {
                _Logging.Warn("ConfigManager GetUserByAccessKey user GUID " + cred.UserGUID + " not found, referenced by credential GUID " + cred.GUID);
                return null;
            }

            return user;
        }

        internal bool AddUser(string guid, string name, string email)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));

            User user = new User(guid, name, email);
            return AddUser(user);
        }

        internal bool AddUser(User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            User userByGuid = GetUserByGuid(user.GUID);
            if (userByGuid != null)
            {
                _Logging.Warn("ConfigManager AddUser user GUID " + user.GUID + " already exists");
                return false;
            }

            User userByEmail = GetUserByEmail(user.Email);
            if (userByEmail != null)
            {
                _Logging.Warn("ConfigManager AddUser user email " + user.Email + " already exists");
                return false;
            }

           return DataAccess.AddUser(user);
        }

        internal void DeleteUser(string guid)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));
            DataAccess.DeleteUser(guid);
        }
         


        internal List<Credential> GetCredentials()
        {
           return  DataAccess.GetCredentials();
        }

        internal bool CredentialGuidExists(string guid)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));

            return DataAccess.CredentialGuidExists(guid);
        }

        internal Credential GetCredentialByGuid(string guid)
        { 
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));

            return DataAccess.GetCredentialByGuid(guid);
        }

        internal List<Credential> GetCredentialsByUser(string userGuid)
        { 
            if (String.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));

            return DataAccess.GetCredentialsByUser(userGuid);
        }

        internal Credential GetCredentialByAccessKey(string accessKey)
        {
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));

            // Check in-memory credentials first (dev/test — no DB required)
            var inMem = _Settings.Credentials.FirstOrDefault(c => c.AccessKey == accessKey);
            if (inMem != null) return inMem;

            return DataAccess.GetCredentialByAccessKey(accessKey);
        }

        internal bool AddCredential(string userGuid, string description, string accessKey, string secretKey, bool isBase64)
        {
            if (String.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            if (String.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey));

            Credential cred = new Credential(userGuid, description, accessKey, secretKey, isBase64);
            return AddCredential(cred);
        }

        internal bool AddCredential(Credential cred)
        {
            if (cred == null) throw new ArgumentNullException(nameof(cred));

            Credential credByGuid = GetCredentialByGuid(cred.GUID);
            if (credByGuid != null)
            {
                _Logging.Warn("ConfigManager AddCredential credential GUID " + cred.GUID + " already exists");
                return false;
            }

            Credential credByKey = GetCredentialByAccessKey(cred.AccessKey);
            if (credByKey != null)
            {
                _Logging.Warn("ConfigManager AddCredential access key " + cred.AccessKey + " already exists");
                return false;
            }

            return DataAccess.AddCredential(cred);
        }

        internal void DeleteCredential(string guid)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));
           DataAccess.DeleteCredential(guid);
        }


        internal List<Bucket> GetBuckets()
        {
            if (_Settings.NoDatabase)
                return _Settings.Buckets.ToList();

            return DataAccess.GetBuckets();
        }

        internal bool BucketExists(string name)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            if (_Settings.NoDatabase)
                return _Settings.Buckets.Any(b => b.Name == name);

           return DataAccess.BucketExists(name);
        }

        internal List<Bucket> GetBucketsByUser(string userGuid)
        { 
            if (String.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));

            if (_Settings.NoDatabase)
                return _Settings.Buckets.Where(b => b.OwnerGUID == userGuid).ToList();

            return DataAccess.GetBucketsByUser(userGuid);
        }

        internal Bucket GetBucketByGuid(string guid)
        { 
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));

            if (_Settings.NoDatabase)
                return _Settings.Buckets.FirstOrDefault(b => b.GUID == guid);

            return DataAccess.GetBucketByGuid(guid);
        }

        internal Bucket GetBucketByName(string name)
        { 
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            if (_Settings.NoDatabase)
                return _Settings.Buckets.FirstOrDefault(b => b.Name == name);

            return DataAccess.GetBucketByName(name);
        }

        internal bool AddBucket(string userGuid, string name)
        {
            if (String.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            Bucket bucket = new Bucket(
                Guid.NewGuid().ToString(),
                name,
                userGuid,
                _Settings.Storage.StorageType,
                _Settings.Storage.DiskDirectory + name + "/Objects");

            return AddBucket(bucket);
        }

        internal bool AddBucket(Bucket bucket)
        {
            if (bucket == null) throw new ArgumentNullException(nameof(bucket));

            if (BucketExists(bucket.Name))
            {
                _Logging.Warn("ConfigManager AddBucket bucket " + bucket.Name + " already exists");
                return false;
            }

            if (_Settings.NoDatabase)
            {
                _Settings.Buckets.Add(bucket);
                return true;
            }

           return DataAccess.AddBucket(bucket);
        }

        internal void DeleteBucket(string guid)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));

            if (_Settings.NoDatabase)
            {
                var b = _Settings.Buckets.FirstOrDefault(b => b.GUID == guid);
                if (b != null) _Settings.Buckets.Remove(b);
                return;
            }

            DataAccess.DeleteBucket(guid);
        }

        #region Internal-Upload-Methods

        internal CosmoS3.Classes.Upload GetUploadByGuid(string uploadGuid)
        {
            if (String.IsNullOrEmpty(uploadGuid)) return null;

           return DataAccess.GetUploadByGuid(uploadGuid);
        }

        internal List<CosmoS3.Classes.Upload> GetUploads()
        {
           return DataAccess.GetUploads();
        }

        internal List<CosmoS3.Classes.Upload> GetUploadsByBucketGuid(string bucketGuid)
        {
            if (String.IsNullOrEmpty(bucketGuid)) return new List<CosmoS3.Classes.Upload>();

            return DataAccess.GetUploadsByBucketGuid(bucketGuid);
        }

        internal void AddUpload(CosmoS3.Classes.Upload upload)
        {
            if (upload == null) throw new ArgumentNullException(nameof(upload));
           DataAccess.AddUpload(upload);
        }

        internal void DeleteUpload(string uploadGuid)
        {
            if (String.IsNullOrEmpty(uploadGuid)) return;

            DataAccess.DeleteUpload(uploadGuid);
        }

        internal void AddUploadPart(UploadPart part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
           DataAccess.AddUploadPart(part);
        }

        internal List<UploadPart> GetUploadPartsByUploadGuid(string uploadGuid)
        {
            if (String.IsNullOrEmpty(uploadGuid)) return null;

            return DataAccess.GetUploadPartsByGuid(uploadGuid);
        }

        internal void DeleteUploadParts(string uploadGuid)
        {
            if (String.IsNullOrEmpty(uploadGuid)) return;

           DataAccess.DeleteUploadParts(uploadGuid);
        }

        #endregion
    }
}
