
using CosmoS3.Classes;
using CosmoS3.Settings;
using CosmoS3.Storage;
using Microsoft.Extensions.Caching.Memory;
using CosmoS3;
using System;
using System.Text.Json;
using Bucket = CosmoS3.Classes.Bucket;
using Upload = CosmoS3.Classes.Upload;
using Obj = CosmoS3.Classes.Obj;

namespace CosmoS3;

public class DataAccess
{
    static JsonSerializerOptions option = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    static SettingsBase _Settings;
    static string conString = "";

    /// <summary>Called by S3Middleware to inject settings instead of reading system.json.</summary>
    public static void Initialize(SettingsBase settings)
    {
        _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        var db = _Settings.Database;
        conString = $"server={db.Hostname},{db.Port};database={db.DatabaseName};user id={db.Username};password={db.Password};TrustServerCertificate=true;";
    }

    static void InitConString()
    {
        if (_Settings == null)
        {
            _Settings = SerializationHelper.DeserializeJson<SettingsBase>(File.ReadAllText("./system.json"));
            var db = _Settings.Database;
            conString = $"server={db.Hostname},{db.Port};database={db.DatabaseName};user id={db.Username};password={db.Password};TrustServerCertificate=true;";
        }
    }
    public static bool SaveObject(Bucket bucket, Obj obj, S3Logger log)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        obj.BucketGUID = bucket.GUID;

        var test = GetObjectLatestMetadata(bucket, obj.Key);
        if (test != null && test.GUID != obj.GUID)
        {
            if (!bucket.EnableVersioning)
            {
                // Caller is responsible for deleting old blob and DB record via BucketClient.AddObject
                // (DataAccess.DeleteObjectRecord). Just set version to 1.
                obj.Version = 1;
            }
            else
            {
                obj.Version = (test.Version + 1);
            }
        }
        else if (test == null)
        {
            obj.Version = 1;
        }

        DateTime ts = DateTime.Now.ToUniversalTime();
        obj.CreatedUtc = ts;
        obj.LastAccessUtc = ts;
        obj.LastUpdateUtc = ts;
        obj.ExpirationUtc = null;
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.Objects_Update", conString))
        using (var con = cmd.Connection)
        {
            var json = MyCommand.ToJson(obj);
            cmd.Parameters.AddWithValue("@json", json);
            con.Open();
            var id = cmd.ExecuteScalar();
            return true;
        }
    }
    public static long GetObjectLatestVersion(string key)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.ObjectLatestVersionByKey", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@key", key);
            con.Open();
            var id = cmd.ExecuteScalar();
            return (long)id;
        }

    }
    public static BucketStatistics GetStatics(Bucket bucket)
    {
        BucketStatistics ret = new BucketStatistics(bucket.Name, bucket.GUID, 0, 0);
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.BucketStatistics", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guide", bucket.GUID);
            con.Open();
            var reader = cmd.ExecuteReader();
            if (reader.Rows.Count > 0)
            {
                ret.Objects = reader.Rows[0][0].AsInt() ?? 0;
                ret.Bytes   = reader.Rows[0][1].AsInt() ?? 0;
            }
        }
        return ret;

    }
    public static Obj? GetObjectLatestMetadata(Bucket bucket, string key)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetObjectLatestMetadata", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@bucketGuid", bucket.GUID);
            cmd.Parameters.AddWithValue("@key", key);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<Obj>(json, option)!;
        }
    }
  
    public static Obj? GetObjectVersionMetadata(Bucket bucket, string key, long version)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetObjectVersionMetadata", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@bucketGuid", bucket.GUID);
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@version", version);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<Obj>(json, option)!;
        }
    }
    public static Obj? GetObjectMetadataByGuid(Bucket bucket, string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetObjectMetadataByGuid", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@bucketGuid", bucket.GUID);
            cmd.Parameters.AddWithValue("@guid", guid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<Obj>(json, option)!;
        }
    }
    private static bool ObjectMarkForDelete(string guid, bool mark)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.ObjectMarkForDelete", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", guid);
            cmd.Parameters.AddWithValue("@marker", mark);
            con.Open();
            var id = cmd.ExecuteNonQuery();
            return true;
        }
    }
    private static bool ObjectDelete(Obj obj, StorageDriverBase storage)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.ObjectDelete", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", obj.GUID);
            con.Open();
            cmd.ExecuteNonQuery();
        }
        storage.Delete(obj.BlobFilename);
        return true;
    }

    /// <summary>
    /// Deletes an object DB record by GUID only (caller handles blob file deletion).
    /// </summary>
    public static void DeleteObjectRecord(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.ObjectDelete", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", guid);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }

    public static bool DeleteLatestObject(Bucket bucket, string key, S3Logger logger, StorageDriverBase storage)
    {
        Obj obj = GetObjectLatestMetadata(bucket, key);
        if (obj == null)
        {
            logger.Debug("Delete unable to find key " + bucket.Name + "/" + key);
            return false;
        }

        if (bucket.EnableVersioning)
        {
            logger.Info("Delete marking key " + bucket.Name + "/" + key + " as deleted");
            obj.DeleteMarker = true;
            return ObjectMarkForDelete(obj.GUID, true);
        }
        else
        {
            logger.Info("Delete deleting key " + bucket.Name + "/" + key);
            return ObjectDelete(obj, storage);
        }
    }
    public static bool DeleteObjectVersion(Bucket bucket, string key, long version, S3Logger logger, StorageDriverBase storage)
    {
        Obj obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            logger.Debug("Delete unable to find key " + bucket.Name + "/" + key + " version " + version);
            return false;
        }

        if (bucket.EnableVersioning)
        {
            logger.Info("Delete marking key " + bucket.Name + "/" + key + " version " + version + " as deleted");
            obj.DeleteMarker = true;
            return ObjectMarkForDelete(obj.GUID, true);
        }
        else
        {
            logger.Info("Delete deleting key " + bucket.Name + "/" + key + " version " + version);
            return ObjectDelete(obj, storage);
        }
    }
    public static bool DeleteObjectVersionMetadata(Bucket bucket, string key, long version, S3Logger logger, StorageDriverBase storage)
    {
        Obj obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            logger.Debug("Delete unable to find key " + bucket.Name + "/" + key + " version " + version);
            return false;
        }

        if (bucket.EnableVersioning)
        {
            logger.Info("Delete marking key " + bucket.Name + "/" + key + " as deleted");
            obj.DeleteMarker = true;
            return ObjectMarkForDelete(obj.GUID, true);
        }
        else
        {
            logger.Info("Delete deleting key " + bucket.Name + "/" + key);
            return ObjectDelete(obj, storage);
        }
    }
    private static void InsertBucketTags(List<BucketTag> tags)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.buckettags_Insert", conString))
        using (var con = cmd.Connection)
        {
            var json = MyCommand.ToJson(tags);
            cmd.Parameters.AddWithValue("@json", json);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    private static void InsertObjectTags(List<ObjectTag> tags)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.ObjectTags_Insert", conString))
        using (var con = cmd.Connection)
        {
            var json = MyCommand.ToJson(tags);
            cmd.Parameters.AddWithValue("@json", json);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }

    public static void AddBucketTags(Bucket bucket, List<BucketTag> tags)
    {
        if (tags != null && tags.Count > 0)
        {
            foreach (BucketTag tag in tags)
            {
                tag.BucketGUID = bucket.GUID;
            }
            InsertBucketTags(tags);
        }
        // Invalidate cache so next read returns fresh data
        _credentialCache.Remove($"get_bucket_tags_{bucket.GUID}");
    }
    public static void DeleteObjectVersionTags(Bucket bucket, string key, long version, S3Logger logger)
    {
        InitConString();
        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            logger.Debug("Exists unable to find key " + bucket.Name + "/" + key + " version " + version);
            return;
        }
        using (var cmd = MyCommand.CmdProc("s3.DeleteObjectVersionTags", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGUID", bucket.GUID);
            cmd.Parameters.AddWithValue("@ObjectGUID", obj.GUID);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    public static void AddObjectVersionTags(Bucket bucket, string key, long version, List<ObjectTag> tags, S3Logger logger)
    {
        DeleteObjectVersionTags(bucket, key, version, logger);

        if (tags != null && tags.Count > 0)
        {
            foreach (ObjectTag tag in tags)
            {
                tag.BucketGUID = bucket.GUID;
            }
            InsertObjectTags(tags);
        }
    }
    public static List<BucketTag> GetBucketTags(Bucket bucket)
    {
        string cacheKey = $"get_bucket_tags_{bucket.GUID}";
        var result = _credentialCache.GetOrCreate(cacheKey, entry => {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            return BucketTagsFromDb(bucket.GUID);
        });

        // result is List<BucketTag> or null
        return result;
    }
    private static List<BucketTag> BucketTagsFromDb(string bucketGuid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetBucketTags", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@bucketGuid", bucketGuid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<BucketTag>>(json, option)!;
        }

    }
    private static List<Obj>? GetObj(string bucketGuid, int nextStandIndex, string prefix, int maxResults)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.Enumerate", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@bucketguid", bucketGuid);
            cmd.Parameters.AddWithValue("@nextStartIndex", nextStandIndex);
            cmd.Parameters.AddWithValue("@maxResults", maxResults);
            cmd.Parameters.AddWithValue("@prefix", prefix);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<Obj>>(json, option)!;
        }
    }
    
    public static void Enumerate(Bucket bucket, string delimiter, string prefix, int startIndex, int maxResults,
            out List<Obj> objects,
            out List<string> prefixes,
            out int nextStartIndex,
            out bool isTruncated)
    {
        objects = new List<Obj>();
        prefixes = new List<string>();
        nextStartIndex = startIndex;
        isTruncated = false;

        while (true)
        {
            List<Obj> tempObjects = GetObj(bucket.GUID, nextStartIndex, prefix, maxResults);

            if (tempObjects == null || tempObjects.Count < 1)
            {
                break;
            }
            foreach (Obj obj in tempObjects)
            {
                string currPrefix = null;
                string tempKey = obj.Key;// new string(obj.Key);
                if (!String.IsNullOrEmpty(prefix)) tempKey = tempKey.Replace(prefix, "");

                if (!String.IsNullOrEmpty(delimiter))
                {
                    if (tempKey.Contains(delimiter))
                    {
                        int delimiterPos = tempKey.IndexOf(delimiter);
                        currPrefix = tempKey.Substring(0, delimiterPos + delimiter.Length);
                        if (!prefixes.Contains(currPrefix))
                        {
                            prefixes.Add(currPrefix);
                        }
                    }
                }

                if (String.IsNullOrEmpty(currPrefix) && objects.Count <= maxResults)
                {
                    objects.Add(obj);
                }

                if (obj.IsFolder && obj.ContentLength == 0)
                {
                    prefixes.Add(obj.Key);
                }

                nextStartIndex = obj.Id + 1;
            }

            if (objects.Count >= maxResults)
            {
                isTruncated = true;
                break;
            }

        }
        // Filter to only the latest version of each key
        List<Obj> latestObjects = new List<Obj>();
        Dictionary<string, Obj> latestByKey = new Dictionary<string, Obj>();
        foreach (Obj obj in objects)
        {
            if (!latestByKey.ContainsKey(obj.Key))
            {
                latestByKey[obj.Key] = obj;
            }
            else if (obj.Version > latestByKey[obj.Key].Version)
            {
                latestByKey[obj.Key] = obj;
            }
        }
        objects = latestByKey.Values.ToList();
        return;
    }
    public static List<ObjectTag> GetObjectTags(Bucket bucket, string key, long version, S3Logger log)
    {
        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("GetTags unable to find key " + bucket.Name + "/" + key + " version " + version);
            return null;
        }

        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetObjectTags", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@version", version);
            cmd.Parameters.AddWithValue("@BucketGUID", bucket.GUID);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<ObjectTag>>(json, option)!;
        }
    }
    public static List<ObjectTag> GetObjectTags(Bucket bucket, string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetObjectTagsByOGuid", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@ObjectGUID", guid);
            cmd.Parameters.AddWithValue("@BucketGUID", bucket.GUID);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<ObjectTag>>(json, option)!;
        }
    }
    public static void DeleteBucketTags(Bucket bucket)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.DeleteBucketTags", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGUID", bucket.GUID);
            con.Open();
            var id = cmd.ExecuteNonQuery();
        }
        // Invalidate cache
        _credentialCache.Remove($"get_bucket_tags_{bucket.GUID}");
    }

    public static bool ObjectGroupAclExists(Bucket bucket, string groupName, string key, long version, S3Logger log)
    {
        if (String.IsNullOrEmpty(groupName)) throw new ArgumentNullException(nameof(groupName));
        if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("Exists unable to find key " + bucket.Name + "/" + key + " version " + version);
            return false;
        }
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.ObjectGroupAclExists", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGUID", bucket.GUID);
            cmd.Parameters.AddWithValue("@UserGroup", groupName);
            cmd.Parameters.AddWithValue("@ObjectGUID", obj.GUID);
            con.Open();
            var ret = (int)cmd.ExecuteScalar();
            return ret == 1;
        }

    }
    public static bool ObjectUserAclExists(Bucket bucket, string userGuid, string key, long version, S3Logger log)
    {

        if (String.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));
        if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("Exists unable to find key " + bucket.Name + "/" + key + " version " + version);
            return false;
        }
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.ObjectUserAclExists", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGUID", bucket.GUID);
            cmd.Parameters.AddWithValue("@UserGUID", userGuid);
            cmd.Parameters.AddWithValue("@ObjectGUID", obj.GUID);
            con.Open();
            var ret = (int)cmd.ExecuteScalar();
            return ret == 1;
        }

    }
    public static bool BucketGroupAclExists(Bucket bucket, string groupName)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.BucketGroupAclExists", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGUID", bucket.GUID);
            cmd.Parameters.AddWithValue("@GroupName", groupName);
            con.Open();
            var ret = (int)cmd.ExecuteScalar();
            return ret == 1;
        }
    }

    public static bool BucketUserAclExists(Bucket bucket, string userGuid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.BucketUserAclExists", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGUID", bucket.GUID);
            cmd.Parameters.AddWithValue("@UserGUID", userGuid);
            con.Open();
            var ret = (int)cmd.ExecuteScalar();
            return ret == 1;
        }
    }
    public static List<BucketAcl>? GetBucketAcl(Bucket bucket)
    {
        string cacheKey = $"getbucket_acl_{bucket.GUID}";
        var result = _credentialCache.GetOrCreate(cacheKey, entry => {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            return GetBucketAclFromDb(bucket);
        });

        // result is List<BucketTag> or null
        return result;
    }
    private static List<BucketAcl>? GetBucketAclFromDb(Bucket bucket)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetBucketAcl", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@bucketGuid", bucket.GUID);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            var ret = JsonSerializer.Deserialize<List<BucketAcl>>(json, option)!;
            return ret;
        }
    }
    public static List<ObjectAcl> GetObjectVersionAcl(Bucket bucket, string key, long version, S3Logger log)
    {
        if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("GetAcl unable to find key " + bucket.Name + "/" + key + " version " + version);
            return null;
        }

        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetObjectVersionAcl", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@bucketGuid", bucket.GUID);
            cmd.Parameters.AddWithValue("@ObjectGUID", obj.GUID);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<ObjectAcl>>(json, option)!;
        }
    }
    public static List<ObjectAcl> GetObjectAcl(Bucket bucket, string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetObjectVersionAcl", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@bucketGuid", bucket.GUID);
            cmd.Parameters.AddWithValue("@ObjectGUID", guid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<ObjectAcl>>(json, option)!;
        }
    }
    private static void InsertBucketAcls(List<BucketAcl> acl)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.bucketacls_Insert", conString))
        using (var con = cmd.Connection)
        {
            var json = MyCommand.ToJson(acl);
            cmd.Parameters.AddWithValue("@json", json);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    private static void InsertObjectAcls(List<ObjectAcl> acls)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.objectacls_Insert", conString))
        using (var con = cmd.Connection)
        {
            var json = MyCommand.ToJson(acls);
            cmd.Parameters.AddWithValue("@json", json);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    public static void AddBucketAcl(Bucket bucket, BucketAcl acl)
    {
        if (acl != null)
        {
            acl.BucketGUID = bucket.GUID;
            var list = new List<BucketAcl>();
            list.Add(acl);
            InsertBucketAcls(list);
        }
    }
    public static void SetBucketAcls(Bucket bucket, List<BucketAcl> acls)
    {
        if (acls != null && acls.Count > 0)
        {
            foreach (BucketAcl acl in acls)
            {
                acl.BucketGUID = bucket.GUID;
            }
            InsertBucketAcls(acls);
        }
    }
    public static void AddObjectAcl(Bucket bucket, ObjectAcl acl, S3Logger log)
    {
        if (acl != null)
        {
            var obj = GetObjectMetadataByGuid(bucket, acl.ObjectGUID);
            if (obj == null)
            {
                log.Debug("SetAcl unable to find object GUID " + acl.ObjectGUID + " in bucket " + bucket.Name);
                return;
            }
            acl.BucketGUID = bucket.GUID;
            acl.ObjectGUID = obj.GUID;
            var lst = new List<ObjectAcl>();
            lst.Add(acl);
            InsertObjectAcls(lst);
        }
    }
    public static void SetObjectAcls(Bucket bucket, string key, long version, List<ObjectAcl> acls, S3Logger log)
    {
        if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("SetAcl unable to find key " + bucket.Name + "/" + key + " version " + version);
            return;
        }

        DeleteObjectVersionAcl(bucket, key, version, log);

        if (acls != null && acls.Count > 0)
        {
            foreach (ObjectAcl acl in acls)
            {
                acl.BucketGUID = bucket.GUID;
                acl.ObjectGUID = obj.GUID;
            }
            InsertObjectAcls(acls);
        }
    }
    public static void DeleteBucketAcl(Bucket bucket)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.DeleteBucketAcl", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGuid", bucket.GUID);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    public static void DeleteObjectVersionAcl(Bucket bucket, string key, long version, S3Logger log)
    {
        if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("DeleteAcl unable to find key " + bucket.Name + "/" + key + " version " + version);
            return;
        }
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.DeleteObjectVersionAcl", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGuid", bucket.GUID);
            cmd.Parameters.AddWithValue("@ObjectGuid", obj.GUID);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    public static void DeleteObjectAcl(Bucket bucket, string key, S3Logger log)
    {
        if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectLatestMetadata(bucket, key);
        if (obj == null)
        {
            log.Debug("DeleteAcl unable to find key " + bucket.Name + "/" + key);
            return;
        }
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.DeleteObjectVersionAcl", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGuid", bucket.GUID);
            cmd.Parameters.AddWithValue("@ObjectGuid", obj.GUID);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }

    public static List<User> GetUsers()
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetUsers", conString))
        using (var con = cmd.Connection)
        {
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<User>>(json, option)!;
        }

    }
    public static bool UserGuidExists(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.UserGuidExists", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@UserGuid", guid);
            con.Open();
            var ret = (int)cmd.ExecuteScalar();
            return ret == 1;
        }
    }
    public static bool UserEmailExists(string email)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.UserEmailExists", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@email", email);
            con.Open();
            var ret = (int)cmd.ExecuteScalar();
            return ret == 1;
        }
    }
    public static User GetUserByGuid(string guid)
    {
        // Check cache first
        string cacheKey = $"getuser_by_guid_{guid}";
        if (_credentialCache.TryGetValue(cacheKey, out User user))
        {
            return user;
        }
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetUserByGuid", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@UserGruid", guid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            var ret = JsonSerializer.Deserialize<User>(json, option)!;
            //Store in cache with 3 - second expiration
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _cacheDuration
            };
            _credentialCache.Set(cacheKey, ret, cacheOptions);
            return ret;
        }
    }
    static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);
    public static User GetUserByName(string name)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetUserByName", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@name", name);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<User>(json)!;
        }
    }
    public static User GetUserByEmail(string email)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetUserByEmail", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@email", email);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<User>(json, option)!;
        }
    }
    public static bool AddUser(User user)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.AddUser", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", user.GUID);
            cmd.Parameters.AddWithValue("@name", user.Name);
            cmd.Parameters.AddWithValue("@email", user.Email);
            cmd.Parameters.AddWithValue("@createdutc", user.CreatedUtc);
            con.Open();
            cmd.ExecuteNonQuery();
        }
        return true;
    }
    public static void DeleteUser(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.DeleteUser", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", guid);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    public static List<Credential> GetCredentials()
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetCredentials", conString))
        using (var con = cmd.Connection)
        {
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<Credential>>(json, option)!;
        }
    }
    public static bool CredentialGuidExists(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.CredentialGuidExists", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@GUID", guid);
            con.Open();
            var ret = (int)cmd.ExecuteScalar();
            return ret == 1;
        }
    }
    public static Credential GetCredentialByGuid(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetCredentialByGuid", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", guid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<Credential>(json, option)!;
        }
    }
    public static List<Credential> GetCredentialsByUser(string userGuid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetCredentialsByUser", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@userguid", userGuid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<Credential>>(json)!;
        }
    }
    static readonly MemoryCache _credentialCache = new(new MemoryCacheOptions());
    public static Credential GetCredentialByAccessKey(string accessKey)
    {
        // Check cache first
        string cacheKey = $"credential_accesskey_{accessKey}";
        if (_credentialCache.TryGetValue(cacheKey, out Credential cachedCredential))
        {
            return cachedCredential;
        }
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetCredentialByAccessKey", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@accessKey", accessKey);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            var ret = JsonSerializer.Deserialize<Credential>(json, option)!;
            // Store in cache with 3-second expiration
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _cacheDuration
            };
            _credentialCache.Set(cacheKey, ret, cacheOptions);

            return ret;
        }
    }
    public static bool AddCredential(Credential cred)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.AddCredential", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", cred.GUID);
            cmd.Parameters.AddWithValue("@userguid", cred.UserGUID);
            cmd.Parameters.AddWithValue("@description", cred.Description);
            cmd.Parameters.AddWithValue("@accessKey", cred.AccessKey);
            cmd.Parameters.AddWithValue("@secretKey", cred.SecretKey);
            cmd.Parameters.AddWithValue("@isbase64", cred.IsBase64);
            cmd.Parameters.AddWithValue("@createdutc", cred.CreatedUtc);

            con.Open();
            cmd.ExecuteNonQuery();
            return true;
        }
    }
    public static void DeleteCredential(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.DeleteCredential", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", guid);

            con.Open();
            cmd.ExecuteNonQuery();
        }
    }

    public static List<Bucket> GetBuckets()
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetBuckets", conString))
        using (var con = cmd.Connection)
        {
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<Bucket>>(json, option)!;
        }
    }
    public static bool BucketExists(string name)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.BucketExists", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@name", name);
            con.Open();
            var ret = (int)cmd.ExecuteScalar();
            return ret == 1;
        }
    }
    public static List<Bucket> GetBucketsByUser(string userGuid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetBucketsByUser", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@OwnerGUID", userGuid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<Bucket>>(json, option)!;
        }
    }
    public static Bucket GetBucketByGuid(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetBucketByGuid", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", guid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<Bucket>(json)!;
        }
    }
    public static Bucket GetBucketByName(string name)
    {
        string cacheKey = $"getbucket_by_name_{name}";
        if (_credentialCache.TryGetValue(cacheKey, out Bucket bucket))
        {
            return bucket;
        }
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetBucketByName", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@name", name);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            var ret= JsonSerializer.Deserialize<Bucket>(json, option)!;
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _cacheDuration
            };
            _credentialCache.Set(cacheKey, ret, cacheOptions);
            return ret;
        }
    }
    public static bool AddBucket(Bucket bucket)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.AddBucket", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", bucket.GUID);
            cmd.Parameters.AddWithValue("@ownerguid", bucket.OwnerGUID);
            cmd.Parameters.AddWithValue("@name", bucket.Name);
            cmd.Parameters.AddWithValue("@regionstring", bucket.RegionString);
            cmd.Parameters.AddWithValue("@storagetype", bucket.StorageType);
            cmd.Parameters.AddWithValue("@diskdirectory", bucket.DiskDirectory);
            cmd.Parameters.AddWithValue("@enableversioning", bucket.EnableVersioning);
            cmd.Parameters.AddWithValue("@enablepublicwrite", bucket.EnablePublicWrite);
            cmd.Parameters.AddWithValue("@enablepublicread", bucket.EnablePublicRead);
            cmd.Parameters.AddWithValue("@createdutc", bucket.CreatedUtc);
            con.Open();
            cmd.ExecuteNonQuery();
            return true;
        }
    }
    public static void DeleteBucket(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.DeleteBucket", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", guid);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    //Newupload
    public static Upload GetUploadByGuid(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetUploadByGuid", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", guid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<Upload>(json, option)!;
        }
    }
    public static List<Upload> GetUploads()
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetUploads", conString))
        using (var con = cmd.Connection)
        {
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<Upload>>(json, option)!;
        }
    }
    public static List<Upload> GetUploadsByBucketGuid(string buggetGID)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetUploadsByBucket", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@BucketGUID", buggetGID);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<Upload>>(json, option)!;
        }
    }
    public static void AddUpload(Upload upload)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.AddUpload", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", upload.GUID);
            cmd.Parameters.AddWithValue("@bucketguid", upload.BucketGUID);
            cmd.Parameters.AddWithValue("@ownerguid", upload.OwnerGUID);
            cmd.Parameters.AddWithValue("@AuthorGUID", upload.AuthorGUID);
            cmd.Parameters.AddWithValue("@key", upload.Key);
            cmd.Parameters.AddWithValue("@CreatedUtc", upload.CreatedUtc);
            cmd.Parameters.AddWithValue("@LastAccessUtc", upload.LastAccessUtc);
            cmd.Parameters.AddWithValue("@ExpirationUtc", upload.ExpirationUtc);
            cmd.Parameters.AddWithValue("@ContentType", upload.ContentType);
            cmd.Parameters.AddWithValue("@Metadata", upload.Metadata);

            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    public static void DeleteUpload(string guid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.DeleteUpload", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@guid", guid);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }
    public static void AddUploadPart(UploadPart part)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.AddUploadPart", conString))
            using (var con = cmd.Connection)
            {
                cmd.Parameters.AddWithValue("@guid", part.GUID);
                cmd.Parameters.AddWithValue("@bucketguid", part.BucketGUID);
                cmd.Parameters.AddWithValue("@ownerguid", part.OwnerGUID);
                cmd.Parameters.AddWithValue("@uploadguid", part.UploadGUID);
                cmd.Parameters.AddWithValue("@partnumber", part.PartNumber);
                cmd.Parameters.AddWithValue("@partlength", part.PartLength);
                cmd.Parameters.AddWithValue("@md5hash", part.MD5Hash);
                cmd.Parameters.AddWithValue("@sha1hash", part.Sha1Hash);
                cmd.Parameters.AddWithValue("@sha256hash", part.Sha256Hash);
                cmd.Parameters.AddWithValue("@lastaccessutc", part.LastAccessUtc);
                cmd.Parameters.AddWithValue("@createdutc", part.CreatedUtc);
                con.Open();
                cmd.ExecuteNonQuery();
        }
    }
    
    public static List<UploadPart> GetUploadPartsByGuid(string uploadGuid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.GetUploadParts", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@uploadguid", uploadGuid);
            con.Open();
            var reader = cmd.ExecuteReader();
            var json = MyCommand.GetJson(reader);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<List<UploadPart>>(json, option)!;
        }
    }
    public static void DeleteUploadParts(string uploadGuid)
    {
        InitConString();
        using (var cmd = MyCommand.CmdProc("s3.DeleteUploadParts", conString))
        using (var con = cmd.Connection)
        {
            cmd.Parameters.AddWithValue("@uploadguid", uploadGuid);
            con.Open();
            cmd.ExecuteNonQuery();
        }
    }

}

