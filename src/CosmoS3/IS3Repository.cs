using CosmoS3.Classes;
using CosmoS3.Storage;
using Bucket = CosmoS3.Classes.Bucket;
using Upload = CosmoS3.Classes.Upload;

namespace CosmoS3;

public interface IS3Repository
{
    // Objects
    bool SaveObject(Bucket bucket, Obj obj, S3Logger log);
    long GetObjectLatestVersion(string key);
    BucketStatistics GetStatics(Bucket bucket);
    Obj? GetObjectLatestMetadata(Bucket bucket, string key);
    Obj? GetObjectVersionMetadata(Bucket bucket, string key, long version);
    Obj? GetObjectMetadataByGuid(Bucket bucket, string guid);
    void DeleteObjectRecord(string guid);
    bool DeleteLatestObject(Bucket bucket, string key, S3Logger logger, StorageDriverBase storage);
    bool DeleteObjectVersion(Bucket bucket, string key, long version, S3Logger logger, StorageDriverBase storage);
    bool DeleteObjectVersionMetadata(Bucket bucket, string key, long version, S3Logger logger, StorageDriverBase storage);
    void Enumerate(Bucket bucket, string delimiter, string prefix, int startIndex, int maxResults,
        out List<Obj> objects, out List<string> prefixes, out int nextStartIndex, out bool isTruncated);

    // Tags
    void AddBucketTags(Bucket bucket, List<BucketTag> tags);
    void AddObjectVersionTags(Bucket bucket, string key, long version, List<ObjectTag> tags, S3Logger log);
    List<BucketTag> GetBucketTags(Bucket bucket);
    List<ObjectTag> GetObjectTags(Bucket bucket, string key, long version, S3Logger log);
    List<ObjectTag> GetObjectTags(Bucket bucket, string guid);
    void DeleteBucketTags(Bucket bucket);
    void DeleteObjectVersionTags(Bucket bucket, string key, long version, S3Logger log);

    // ACLs
    bool ObjectGroupAclExists(Bucket bucket, string groupName, string key, long version, S3Logger log);
    bool ObjectUserAclExists(Bucket bucket, string userGuid, string key, long version, S3Logger log);
    bool BucketGroupAclExists(Bucket bucket, string groupName);
    bool BucketUserAclExists(Bucket bucket, string userGuid);
    List<BucketAcl> GetBucketAcl(Bucket bucket);
    List<ObjectAcl> GetObjectVersionAcl(Bucket bucket, string key, long version, S3Logger log);
    void SetBucketAcls(Bucket bucket, List<BucketAcl> acls);
    void SetObjectAcls(Bucket bucket, string key, long version, List<ObjectAcl> acls, S3Logger log);
    void DeleteBucketAcl(Bucket bucket);
    void DeleteObjectVersionAcl(Bucket bucket, string key, long version, S3Logger log);
    List<ObjectAcl> GetObjectAcl(Bucket bucket, string guid);
    void AddBucketAcl(Bucket bucket, BucketAcl acl);
    void AddObjectAcl(Bucket bucket, ObjectAcl acl, S3Logger log);
    void DeleteObjectAcl(Bucket bucket, string key, S3Logger log);

    // Users
    List<User> GetUsers();
    bool UserGuidExists(string guid);
    bool UserEmailExists(string email);
    User? GetUserByGuid(string guid);
    User? GetUserByName(string name);
    User? GetUserByEmail(string email);
    User AddUser(User user);
    void DeleteUser(string guid);

    // Credentials
    List<Credential> GetCredentials();
    bool CredentialGuidExists(string guid);
    Credential? GetCredentialByGuid(string guid);
    List<Credential> GetCredentialsByUser(string userGuid);
    Credential? GetCredentialByAccessKey(string accessKey);
    Credential AddCredential(Credential cred);
    void DeleteCredential(string guid);

    // Buckets
    List<Bucket> GetBuckets();
    bool BucketExists(string name);
    List<Bucket> GetBucketsByUser(string userGuid);
    Bucket? GetBucketByGuid(string guid);
    Bucket? GetBucketByName(string name);
    Bucket AddBucket(Bucket bucket);
    void DeleteBucket(string guid);

    // Multipart Uploads
    List<Upload> GetUploads();
    List<Upload> GetUploadsByBucketGuid(string bucketGuid);
    Upload? GetUploadByGuid(string guid);
    Upload AddUpload(Upload upload);
    void DeleteUpload(string guid);
    void AddUploadPart(UploadPart part);
    List<UploadPart> GetUploadPartsByGuid(string uploadGuid);
    void DeleteUploadParts(string uploadGuid);
}
