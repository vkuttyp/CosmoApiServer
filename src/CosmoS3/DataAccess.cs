using CosmoS3.Classes;
using CosmoS3.Settings;
using CosmoS3.Storage;
using Bucket = CosmoS3.Classes.Bucket;
using Upload = CosmoS3.Classes.Upload;
using Obj = CosmoS3.Classes.Obj;

namespace CosmoS3;

/// <summary>
/// Static facade over <see cref="IS3Repository"/>. All callers (BucketClient, AuthManager,
/// Admin handlers) remain unchanged; the database engine is selected via
/// <see cref="DatabaseSettings.DatabaseType"/> at startup.
/// </summary>
public class DataAccess
{
    static IS3Repository _repo;

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Called once at startup. Creates the appropriate database pool and repository
    /// based on <see cref="DatabaseSettings.DatabaseType"/>.
    /// </summary>
    public static void Initialize(SettingsBase settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        _repo = DatabaseFactory.Create(settings.Database);
    }

    // ── Objects ───────────────────────────────────────────────────────────────

    public static bool SaveObject(Bucket bucket, Obj obj, S3Logger log)
        => _repo.SaveObject(bucket, obj, log);

    public static long GetObjectLatestVersion(string key)
        => _repo.GetObjectLatestVersion(key);

    public static BucketStatistics GetStatics(Bucket bucket)
        => _repo.GetStatics(bucket);

    public static Obj GetObjectLatestMetadata(Bucket bucket, string key)
        => _repo.GetObjectLatestMetadata(bucket, key);

    public static Obj GetObjectVersionMetadata(Bucket bucket, string key, long version)
        => _repo.GetObjectVersionMetadata(bucket, key, version);

    public static Obj GetObjectMetadataByGuid(Bucket bucket, string guid)
        => _repo.GetObjectMetadataByGuid(bucket, guid);

    public static void DeleteObjectRecord(string guid)
        => _repo.DeleteObjectRecord(guid);

    public static bool DeleteLatestObject(Bucket bucket, string key, S3Logger logger, StorageDriverBase storage)
        => _repo.DeleteLatestObject(bucket, key, logger, storage);

    public static bool DeleteObjectVersion(Bucket bucket, string key, long version, S3Logger logger, StorageDriverBase storage)
        => _repo.DeleteObjectVersion(bucket, key, version, logger, storage);

    public static bool DeleteObjectVersionMetadata(Bucket bucket, string key, long version, S3Logger logger, StorageDriverBase storage)
        => _repo.DeleteObjectVersionMetadata(bucket, key, version, logger, storage);

    public static void Enumerate(Bucket bucket, string delimiter, string prefix, int startIndex, int maxResults,
        out List<Obj> objects, out List<string> prefixes, out int nextStartIndex, out bool isTruncated)
        => _repo.Enumerate(bucket, delimiter, prefix, startIndex, maxResults, out objects, out prefixes, out nextStartIndex, out isTruncated);

    // ── Tags ──────────────────────────────────────────────────────────────────

    public static void AddBucketTags(Bucket bucket, List<BucketTag> tags)
        => _repo.AddBucketTags(bucket, tags);

    public static void AddObjectVersionTags(Bucket bucket, string key, long version, List<ObjectTag> tags, S3Logger log)
        => _repo.AddObjectVersionTags(bucket, key, version, tags, log);

    public static List<BucketTag> GetBucketTags(Bucket bucket)
        => _repo.GetBucketTags(bucket);

    public static List<ObjectTag> GetObjectTags(Bucket bucket, string key, long version, S3Logger log)
        => _repo.GetObjectTags(bucket, key, version, log);

    public static List<ObjectTag> GetObjectTags(Bucket bucket, string guid)
        => _repo.GetObjectTags(bucket, guid);

    public static void DeleteBucketTags(Bucket bucket)
        => _repo.DeleteBucketTags(bucket);

    public static void DeleteObjectVersionTags(Bucket bucket, string key, long version, S3Logger log)
        => _repo.DeleteObjectVersionTags(bucket, key, version, log);

    // ── ACLs ─────────────────────────────────────────────────────────────────

    public static bool ObjectGroupAclExists(Bucket bucket, string groupName, string key, long version, S3Logger log)
        => _repo.ObjectGroupAclExists(bucket, groupName, key, version, log);

    public static bool ObjectUserAclExists(Bucket bucket, string userGuid, string key, long version, S3Logger log)
        => _repo.ObjectUserAclExists(bucket, userGuid, key, version, log);

    public static bool BucketGroupAclExists(Bucket bucket, string groupName)
        => _repo.BucketGroupAclExists(bucket, groupName);

    public static bool BucketUserAclExists(Bucket bucket, string userGuid)
        => _repo.BucketUserAclExists(bucket, userGuid);

    public static List<BucketAcl> GetBucketAcl(Bucket bucket)
        => _repo.GetBucketAcl(bucket);

    public static List<ObjectAcl> GetObjectVersionAcl(Bucket bucket, string key, long version, S3Logger log)
        => _repo.GetObjectVersionAcl(bucket, key, version, log);

    public static List<ObjectAcl> GetObjectAcl(Bucket bucket, string guid)
        => _repo.GetObjectAcl(bucket, guid);

    public static void AddBucketAcl(Bucket bucket, BucketAcl acl)
        => _repo.AddBucketAcl(bucket, acl);

    public static void SetBucketAcls(Bucket bucket, List<BucketAcl> acls)
        => _repo.SetBucketAcls(bucket, acls);

    public static void AddObjectAcl(Bucket bucket, ObjectAcl acl, S3Logger log)
        => _repo.AddObjectAcl(bucket, acl, log);

    public static void SetObjectAcls(Bucket bucket, string key, long version, List<ObjectAcl> acls, S3Logger log)
        => _repo.SetObjectAcls(bucket, key, version, acls, log);

    public static void DeleteBucketAcl(Bucket bucket)
        => _repo.DeleteBucketAcl(bucket);

    public static void DeleteObjectVersionAcl(Bucket bucket, string key, long version, S3Logger log)
        => _repo.DeleteObjectVersionAcl(bucket, key, version, log);

    public static void DeleteObjectAcl(Bucket bucket, string key, S3Logger log)
        => _repo.DeleteObjectAcl(bucket, key, log);

    // ── Users ─────────────────────────────────────────────────────────────────

    public static List<User> GetUsers()
        => _repo.GetUsers();

    public static bool UserGuidExists(string guid)
        => _repo.UserGuidExists(guid);

    public static bool UserEmailExists(string email)
        => _repo.UserEmailExists(email);

    public static User GetUserByGuid(string guid)
        => _repo.GetUserByGuid(guid);

    public static User GetUserByName(string name)
        => _repo.GetUserByName(name);

    public static User GetUserByEmail(string email)
        => _repo.GetUserByEmail(email);

    public static bool AddUser(User user)
    {
        _repo.AddUser(user);
        return true;
    }

    public static void DeleteUser(string guid)
        => _repo.DeleteUser(guid);

    // ── Credentials ───────────────────────────────────────────────────────────

    public static List<Credential> GetCredentials()
        => _repo.GetCredentials();

    public static bool CredentialGuidExists(string guid)
        => _repo.CredentialGuidExists(guid);

    public static Credential GetCredentialByGuid(string guid)
        => _repo.GetCredentialByGuid(guid);

    public static List<Credential> GetCredentialsByUser(string userGuid)
        => _repo.GetCredentialsByUser(userGuid);

    public static Credential GetCredentialByAccessKey(string accessKey)
        => _repo.GetCredentialByAccessKey(accessKey);

    public static bool AddCredential(Credential cred)
    {
        _repo.AddCredential(cred);
        return true;
    }

    public static void DeleteCredential(string guid)
        => _repo.DeleteCredential(guid);

    // ── Buckets ───────────────────────────────────────────────────────────────

    public static List<Bucket> GetBuckets()
        => _repo.GetBuckets();

    public static bool BucketExists(string name)
        => _repo.BucketExists(name);

    public static List<Bucket> GetBucketsByUser(string userGuid)
        => _repo.GetBucketsByUser(userGuid);

    public static Bucket GetBucketByGuid(string guid)
        => _repo.GetBucketByGuid(guid);

    public static Bucket GetBucketByName(string name)
        => _repo.GetBucketByName(name);

    public static bool AddBucket(Bucket bucket)
    {
        _repo.AddBucket(bucket);
        return true;
    }

    public static void DeleteBucket(string guid)
        => _repo.DeleteBucket(guid);

    // ── Multipart Uploads ─────────────────────────────────────────────────────

    public static List<Upload> GetUploads()
        => _repo.GetUploads();

    public static List<Upload> GetUploadsByBucketGuid(string bucketGuid)
        => _repo.GetUploadsByBucketGuid(bucketGuid);

    public static Upload GetUploadByGuid(string guid)
        => _repo.GetUploadByGuid(guid);

    public static void AddUpload(Upload upload)
        => _repo.AddUpload(upload);

    public static void DeleteUpload(string guid)
        => _repo.DeleteUpload(guid);

    public static void AddUploadPart(UploadPart part)
        => _repo.AddUploadPart(part);

    public static List<UploadPart> GetUploadPartsByGuid(string uploadGuid)
        => _repo.GetUploadPartsByGuid(uploadGuid);

    public static void DeleteUploadParts(string uploadGuid)
        => _repo.DeleteUploadParts(uploadGuid);
}
