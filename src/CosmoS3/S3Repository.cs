using CosmoS3.Classes;
using CosmoS3.Storage;
using CosmoSQLClient.Core;
using Bucket = CosmoS3.Classes.Bucket;
using Upload = CosmoS3.Classes.Upload;

namespace CosmoS3;

public class S3Repository : IS3Repository
{
    private readonly ISqlDatabase _db;
    private readonly string _t;

    public S3Repository(ISqlDatabase db, string tablePrefix)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _t = tablePrefix ?? throw new ArgumentNullException(nameof(tablePrefix));
    }

    // -------------------------------------------------------------------------
    // Row mappers
    // -------------------------------------------------------------------------

    private static Obj RowToObj(SqlRow r)
    {
        var obj = new Obj
        {
            GUID            = r["guid"].AsString(),
            BucketGUID      = r["bucketguid"].AsString(),
            OwnerGUID       = r["ownerguid"].AsString(),
            AuthorGUID      = r["authorguid"].AsString(),
            Key             = r["objectkey"].AsString(),
            ContentLength   = r["contentlength"].AsInt() ?? 0,
            Version         = r["version"].AsInt() ?? 0,
            Etag            = r["etag"].AsString(),
            BlobFilename    = r["blobfilename"].AsString(),
            IsFolder        = r["isfolder"].AsInt() != 0,
            DeleteMarker    = r["deletemarker"].AsInt() != 0,
            Md5             = r["md5"].AsString(),
            CreatedUtc      = r["createdutc"].AsDate() ?? DateTime.UtcNow,
            LastUpdateUtc   = r["lastupdateutc"].AsDate() ?? DateTime.UtcNow,
            LastAccessUtc   = r["lastaccessutc"].AsDate() ?? DateTime.UtcNow,
            Metadata        = r["metadata"].AsString(),
        };

        Enum.TryParse<RetentionType>(r["retention"].AsString(), out var ret);
        obj.Retention = ret;

        var expStr = r["expirationutc"].IsNull ? null : r["expirationutc"].AsString();
        obj.ExpirationUtc = string.IsNullOrEmpty(expStr)
            ? (DateTime?)null
            : DateTime.Parse(expStr, null, System.Globalization.DateTimeStyles.RoundtripKind);

        return obj;
    }

    private static Bucket RowToBucket(SqlRow r) => new Bucket
    {
        GUID              = r["guid"].AsString(),
        OwnerGUID         = r["ownerguid"].AsString(),
        Name              = r["name"].AsString(),
        RegionString      = r["regionstring"].AsString(),
        StorageType       = Enum.TryParse<StorageDriverType>(r["storagetype"].AsString(), out var st) ? st : StorageDriverType.Disk,
        DiskDirectory     = r["diskdirectory"].AsString(),
        EnableVersioning  = r["enableversioning"].AsInt() != 0,
        EnablePublicWrite = r["enablepublicwrite"].AsInt() != 0,
        EnablePublicRead  = r["enablepublicread"].AsInt() != 0,
        CreatedUtc        = r["createdutc"].AsDate() ?? DateTime.UtcNow,
    };

    private static User RowToUser(SqlRow r) => new User
    {
        GUID       = r["guid"].AsString(),
        Name       = r["name"].AsString(),
        Email      = r["email"].AsString(),
        CreatedUtc = r["createdutc"].AsDate() ?? DateTime.UtcNow,
    };

    private static Credential RowToCredential(SqlRow r) => new Credential
    {
        GUID        = r["guid"].AsString(),
        UserGUID    = r["userguid"].AsString(),
        Description = r["description"].AsString(),
        AccessKey   = r["accesskey"].AsString(),
        SecretKey   = r["secretkey"].AsString(),
        IsBase64    = r["isbase64"].AsInt() != 0,
        CreatedUtc  = r["createdutc"].AsDate() ?? DateTime.UtcNow,
    };

    private static BucketTag RowToBucketTag(SqlRow r) => new BucketTag
    {
        GUID       = r["guid"].AsString(),
        BucketGUID = r["bucketguid"].AsString(),
        Key        = r["tagkey"].AsString(),
        Value      = r["tagvalue"].AsString(),
        CreatedUtc = r["createdutc"].AsDate() ?? DateTime.UtcNow,
    };

    private static ObjectTag RowToObjectTag(SqlRow r) => new ObjectTag
    {
        GUID       = r["guid"].AsString(),
        BucketGUID = r["bucketguid"].AsString(),
        ObjectGUID = r["objectguid"].AsString(),
        Key        = r["tagkey"].AsString(),
        Value      = r["tagvalue"].AsString(),
        CreatedUtc = r["createdutc"].AsDate() ?? DateTime.UtcNow,
    };

    private static BucketAcl RowToBucketAcl(SqlRow r) => new BucketAcl
    {
        GUID             = r["guid"].AsString(),
        UserGroup        = r["usergroup"].AsString(),
        BucketGUID       = r["bucketguid"].AsString(),
        UserGUID         = r["userguid"].AsString(),
        IssuedByUserGUID = r["issuedbyuserguid"].AsString(),
        PermitRead       = r["permitread"].AsInt() != 0,
        PermitWrite      = r["permitwrite"].AsInt() != 0,
        PermitReadAcp    = r["permitreadacp"].AsInt() != 0,
        PermitWriteAcp   = r["permitwriteacp"].AsInt() != 0,
        FullControl      = r["permitfullcontrol"].AsInt() != 0,
        CreatedUtc       = r["createdutc"].AsDate() ?? DateTime.UtcNow,
    };

    private static ObjectAcl RowToObjectAcl(SqlRow r) => new ObjectAcl
    {
        GUID             = r["guid"].AsString(),
        UserGroup        = r["usergroup"].AsString(),
        UserGUID         = r["userguid"].AsString(),
        IssuedByUserGUID = r["issuedbyuserguid"].AsString(),
        BucketGUID       = r["bucketguid"].AsString(),
        ObjectGUID       = r["objectguid"].AsString(),
        PermitRead       = r["permitread"].AsInt() != 0,
        PermitWrite      = r["permitwrite"].AsInt() != 0,
        PermitReadAcp    = r["permitreadacp"].AsInt() != 0,
        PermitWriteAcp   = r["permitwriteacp"].AsInt() != 0,
        FullControl      = r["permitfullcontrol"].AsInt() != 0,
        CreatedUtc       = r["createdutc"].AsDate() ?? DateTime.UtcNow,
    };

    private static Upload RowToUpload(SqlRow r) => new Upload
    {
        GUID          = r["guid"].AsString(),
        BucketGUID    = r["bucketguid"].AsString(),
        OwnerGUID     = r["ownerguid"].AsString(),
        AuthorGUID    = r["authorguid"].AsString(),
        Key           = r["objectkey"].AsString(),
        CreatedUtc    = r["createdutc"].AsDate() ?? DateTime.UtcNow,
        LastAccessUtc = r["lastaccessutc"].AsDate() ?? DateTime.UtcNow,
        ExpirationUtc = r["expirationutc"].AsDate() ?? DateTime.UtcNow,
        ContentType   = r["contenttype"].AsString(),
        Metadata      = r["metadata"].AsString(),
    };

    private static UploadPart RowToUploadPart(SqlRow r) => new UploadPart
    {
        GUID          = r["guid"].AsString(),
        BucketGUID    = r["bucketguid"].AsString(),
        OwnerGUID     = r["ownerguid"].AsString(),
        UploadGUID    = r["uploadguid"].AsString(),
        PartNumber    = (int)(r["partnumber"].AsInt() ?? 0),
        PartLength    = (int)(r["partlength"].AsInt() ?? 0),
        MD5Hash       = r["md5hash"].AsString(),
        Sha1Hash      = r["sha1hash"].AsString(),
        Sha256Hash    = r["sha256hash"].AsString(),
        LastAccessUtc = r["lastaccessutc"].AsDate() ?? DateTime.UtcNow,
        CreatedUtc    = r["createdutc"].AsDate() ?? DateTime.UtcNow,
    };

    // -------------------------------------------------------------------------
    // Objects
    // -------------------------------------------------------------------------

    public bool SaveObject(Bucket bucket, Obj obj, S3Logger log)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        obj.BucketGUID = bucket.GUID;

        var existing = GetObjectLatestMetadata(bucket, obj.Key);
        if (existing != null && existing.GUID != obj.GUID)
        {
            obj.Version = bucket.EnableVersioning ? existing.Version + 1 : 1;
        }
        else if (existing == null)
        {
            obj.Version = 1;
        }

        var ts = DateTime.UtcNow;
        obj.CreatedUtc    = ts;
        obj.LastAccessUtc = ts;
        obj.LastUpdateUtc = ts;
        obj.ExpirationUtc = null;

        var checkRows = _db.QueryAsync(
            $"SELECT guid FROM {_t}objects WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(obj.GUID)) })
            .GetAwaiter().GetResult();

        if (checkRows.Count == 0)
        {
            _db.ExecuteAsync(
                $"INSERT INTO {_t}objects " +
                "(guid, bucketguid, ownerguid, authorguid, objectkey, contenttype, contentlength, version, etag, retention, blobfilename, isfolder, deletemarker, md5, createdutc, lastupdateutc, lastaccessutc, metadata, expirationutc) " +
                "VALUES (@guid, @bucketguid, @ownerguid, @authorguid, @objectkey, @contenttype, @contentlength, @version, @etag, @retention, @blobfilename, @isfolder, @deletemarker, @md5, @createdutc, @lastupdateutc, @lastaccessutc, @metadata, @expirationutc)",
                ObjParams(obj)).GetAwaiter().GetResult();
        }
        else
        {
            _db.ExecuteAsync(
                $"UPDATE {_t}objects SET " +
                "bucketguid=@bucketguid, ownerguid=@ownerguid, authorguid=@authorguid, objectkey=@objectkey, contenttype=@contenttype, contentlength=@contentlength, version=@version, etag=@etag, retention=@retention, blobfilename=@blobfilename, isfolder=@isfolder, deletemarker=@deletemarker, md5=@md5, createdutc=@createdutc, lastupdateutc=@lastupdateutc, lastaccessutc=@lastaccessutc, metadata=@metadata, expirationutc=@expirationutc " +
                "WHERE guid=@guid",
                ObjParams(obj)).GetAwaiter().GetResult();
        }

        return true;
    }

    private static IReadOnlyList<SqlParameter> ObjParams(Obj o) => new[]
    {
        SqlParameter.Named("guid",          SqlValue.From(o.GUID)),
        SqlParameter.Named("bucketguid",    SqlValue.From(o.BucketGUID)),
        SqlParameter.Named("ownerguid",     SqlValue.From(o.OwnerGUID)),
        SqlParameter.Named("authorguid",    SqlValue.From(o.AuthorGUID)),
        SqlParameter.Named("objectkey",     SqlValue.From(o.Key)),
        SqlParameter.Named("contenttype",   SqlValue.From(o.ContentType)),
        SqlParameter.Named("contentlength", SqlValue.From(o.ContentLength)),
        SqlParameter.Named("version",       SqlValue.From(o.Version)),
        SqlParameter.Named("etag",          o.Etag != null ? SqlValue.From(o.Etag) : SqlValue.Null_),
        SqlParameter.Named("retention",     SqlValue.From(o.Retention.ToString())),
        SqlParameter.Named("blobfilename",  o.BlobFilename != null ? SqlValue.From(o.BlobFilename) : SqlValue.Null_),
        SqlParameter.Named("isfolder",      SqlValue.From(o.IsFolder ? 1 : 0)),
        SqlParameter.Named("deletemarker",  SqlValue.From(o.DeleteMarker ? 1 : 0)),
        SqlParameter.Named("md5",           o.Md5 != null ? SqlValue.From(o.Md5) : SqlValue.Null_),
        SqlParameter.Named("createdutc",    SqlValue.From(o.CreatedUtc)),
        SqlParameter.Named("lastupdateutc", SqlValue.From(o.LastUpdateUtc)),
        SqlParameter.Named("lastaccessutc", SqlValue.From(o.LastAccessUtc)),
        SqlParameter.Named("metadata",      o.Metadata != null ? SqlValue.From(o.Metadata) : SqlValue.Null_),
        SqlParameter.Named("expirationutc", o.ExpirationUtc.HasValue ? SqlValue.From(o.ExpirationUtc.Value) : SqlValue.Null_),
    };

    public long GetObjectLatestVersion(string key)
    {
        var rows = _db.QueryAsync(
            $"SELECT MAX(version) AS v FROM {_t}objects WHERE objectkey = @key",
            new[] { SqlParameter.Named("key", SqlValue.From(key)) })
            .GetAwaiter().GetResult();

        if (rows.Count == 0 || rows[0]["v"].IsNull) return 0;
        return rows[0]["v"].AsInt() ?? 0;
    }

    public BucketStatistics GetStatics(Bucket bucket)
    {
        var rows = _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt, COALESCE(SUM(contentlength), 0) AS bytes FROM {_t}objects WHERE bucketguid = @bg AND deletemarker = 0",
            new[] { SqlParameter.Named("bg", SqlValue.From(bucket.GUID)) })
            .GetAwaiter().GetResult();

        long objects = 0, bytes = 0;
        if (rows.Count > 0)
        {
            objects = rows[0]["cnt"].AsInt() ?? 0;
            bytes   = rows[0]["bytes"].AsInt() ?? 0;
        }
        return new BucketStatistics(bucket.Name, bucket.GUID, objects, bytes);
    }

    public Obj? GetObjectLatestMetadata(Bucket bucket, string key)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}objects WHERE bucketguid = @bg AND objectkey = @key AND deletemarker = 0 ORDER BY version DESC",
            new[]
            {
                SqlParameter.Named("bg",  SqlValue.From(bucket.GUID)),
                SqlParameter.Named("key", SqlValue.From(key)),
            }).GetAwaiter().GetResult();

        return rows.Count > 0 ? RowToObj(rows[0]) : null;
    }

    public Obj? GetObjectVersionMetadata(Bucket bucket, string key, long version)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}objects WHERE bucketguid = @bg AND objectkey = @key AND version = @ver",
            new[]
            {
                SqlParameter.Named("bg",  SqlValue.From(bucket.GUID)),
                SqlParameter.Named("key", SqlValue.From(key)),
                SqlParameter.Named("ver", SqlValue.From(version)),
            }).GetAwaiter().GetResult();

        return rows.Count > 0 ? RowToObj(rows[0]) : null;
    }

    public Obj? GetObjectMetadataByGuid(Bucket bucket, string guid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}objects WHERE bucketguid = @bg AND guid = @guid",
            new[]
            {
                SqlParameter.Named("bg",   SqlValue.From(bucket.GUID)),
                SqlParameter.Named("guid", SqlValue.From(guid)),
            }).GetAwaiter().GetResult();

        return rows.Count > 0 ? RowToObj(rows[0]) : null;
    }

    public void DeleteObjectRecord(string guid)
    {
        _db.ExecuteAsync(
            $"DELETE FROM {_t}objects WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
    }

    private void ObjectMarkForDelete(string guid, bool mark)
    {
        _db.ExecuteAsync(
            $"UPDATE {_t}objects SET deletemarker = @dm WHERE guid = @guid",
            new[]
            {
                SqlParameter.Named("dm",   SqlValue.From(mark ? 1 : 0)),
                SqlParameter.Named("guid", SqlValue.From(guid)),
            }).GetAwaiter().GetResult();
    }

    private void ObjectDelete(Obj obj, StorageDriverBase storage)
    {
        DeleteObjectRecord(obj.GUID);
        storage.Delete(obj.BlobFilename);
    }

    public bool DeleteLatestObject(Bucket bucket, string key, S3Logger logger, StorageDriverBase storage)
    {
        var obj = GetObjectLatestMetadata(bucket, key);
        if (obj == null)
        {
            logger.Debug("Delete unable to find key " + bucket.Name + "/" + key);
            return false;
        }

        if (bucket.EnableVersioning)
        {
            logger.Info("Delete marking key " + bucket.Name + "/" + key + " as deleted");
            ObjectMarkForDelete(obj.GUID, true);
        }
        else
        {
            logger.Info("Delete deleting key " + bucket.Name + "/" + key);
            ObjectDelete(obj, storage);
        }
        return true;
    }

    public bool DeleteObjectVersion(Bucket bucket, string key, long version, S3Logger logger, StorageDriverBase storage)
    {
        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            logger.Debug("Delete unable to find key " + bucket.Name + "/" + key + " version " + version);
            return false;
        }

        if (bucket.EnableVersioning)
        {
            logger.Info("Delete marking key " + bucket.Name + "/" + key + " version " + version + " as deleted");
            ObjectMarkForDelete(obj.GUID, true);
        }
        else
        {
            logger.Info("Delete deleting key " + bucket.Name + "/" + key + " version " + version);
            ObjectDelete(obj, storage);
        }
        return true;
    }

    public bool DeleteObjectVersionMetadata(Bucket bucket, string key, long version, S3Logger logger, StorageDriverBase storage)
    {
        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            logger.Debug("Delete unable to find key " + bucket.Name + "/" + key + " version " + version);
            return false;
        }

        if (bucket.EnableVersioning)
        {
            logger.Info("Delete marking key " + bucket.Name + "/" + key + " as deleted");
            ObjectMarkForDelete(obj.GUID, true);
        }
        else
        {
            logger.Info("Delete deleting key " + bucket.Name + "/" + key);
            ObjectDelete(obj, storage);
        }
        return true;
    }

    public void Enumerate(Bucket bucket, string delimiter, string prefix, int startIndex, int maxResults,
        out List<Obj> objects, out List<string> prefixes, out int nextStartIndex, out bool isTruncated)
    {
        objects         = new List<Obj>();
        prefixes        = new List<string>();
        nextStartIndex  = startIndex;
        isTruncated     = false;

        var sqlPrefix = string.IsNullOrEmpty(prefix) ? "%" : prefix + "%";
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}objects WHERE bucketguid = @bg AND objectkey LIKE @prefix ORDER BY objectkey",
            new[]
            {
                SqlParameter.Named("bg",     SqlValue.From(bucket.GUID)),
                SqlParameter.Named("prefix", SqlValue.From(sqlPrefix)),
            }).GetAwaiter().GetResult();

        if (rows.Count == 0) return;

        // Collect latest version per key
        var latestByKey = new Dictionary<string, Obj>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var o = RowToObj(r);
            if (!latestByKey.TryGetValue(o.Key, out var prev) || o.Version > prev.Version)
                latestByKey[o.Key] = o;
        }

        var allObjs = latestByKey.Values.OrderBy(o => o.Key).ToList();
        int idx = 0;

        foreach (var obj in allObjs)
        {
            if (idx < startIndex) { idx++; continue; }

            string? currPrefix = null;
            string tempKey = obj.Key;
            if (!string.IsNullOrEmpty(prefix))
                tempKey = tempKey.Length > prefix.Length ? tempKey.Substring(prefix.Length) : string.Empty;

            if (!string.IsNullOrEmpty(delimiter) && tempKey.Contains(delimiter))
            {
                int delimPos = tempKey.IndexOf(delimiter);
                currPrefix = (string.IsNullOrEmpty(prefix) ? "" : prefix) + tempKey.Substring(0, delimPos + delimiter.Length);
                if (!prefixes.Contains(currPrefix))
                    prefixes.Add(currPrefix);
            }

            if (string.IsNullOrEmpty(currPrefix))
            {
                if (obj.IsFolder && obj.ContentLength == 0)
                {
                    if (!prefixes.Contains(obj.Key))
                        prefixes.Add(obj.Key);
                }
                else
                {
                    objects.Add(obj);
                }
            }

            nextStartIndex = idx + 1;
            idx++;

            if (objects.Count >= maxResults)
            {
                isTruncated = true;
                break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Tags
    // -------------------------------------------------------------------------

    public void AddBucketTags(Bucket bucket, List<BucketTag> tags)
    {
        if (tags == null || tags.Count == 0) return;
        foreach (var tag in tags)
        {
            tag.BucketGUID = bucket.GUID;
            _db.ExecuteAsync(
                $"INSERT INTO {_t}buckettags (guid, bucketguid, tagkey, tagvalue, createdutc) VALUES (@guid, @bg, @k, @v, @ts)",
                new[]
                {
                    SqlParameter.Named("guid", SqlValue.From(tag.GUID)),
                    SqlParameter.Named("bg",   SqlValue.From(bucket.GUID)),
                    SqlParameter.Named("k",    SqlValue.From(tag.Key)),
                    SqlParameter.Named("v",    tag.Value != null ? SqlValue.From(tag.Value) : SqlValue.Null_),
                    SqlParameter.Named("ts",   SqlValue.From(tag.CreatedUtc)),
                }).GetAwaiter().GetResult();
        }
    }

    public void AddObjectVersionTags(Bucket bucket, string key, long version, List<ObjectTag> tags, S3Logger log)
    {
        DeleteObjectVersionTags(bucket, key, version, log);
        if (tags == null || tags.Count == 0) return;
        foreach (var tag in tags)
        {
            tag.BucketGUID = bucket.GUID;
            _db.ExecuteAsync(
                $"INSERT INTO {_t}objecttags (guid, bucketguid, objectguid, tagkey, tagvalue, createdutc) VALUES (@guid, @bg, @og, @k, @v, @ts)",
                new[]
                {
                    SqlParameter.Named("guid", SqlValue.From(tag.GUID)),
                    SqlParameter.Named("bg",   SqlValue.From(bucket.GUID)),
                    SqlParameter.Named("og",   SqlValue.From(tag.ObjectGUID)),
                    SqlParameter.Named("k",    SqlValue.From(tag.Key)),
                    SqlParameter.Named("v",    tag.Value != null ? SqlValue.From(tag.Value) : SqlValue.Null_),
                    SqlParameter.Named("ts",   SqlValue.From(tag.CreatedUtc)),
                }).GetAwaiter().GetResult();
        }
    }

    public List<BucketTag> GetBucketTags(Bucket bucket)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}buckettags WHERE bucketguid = @bg",
            new[] { SqlParameter.Named("bg", SqlValue.From(bucket.GUID)) })
            .GetAwaiter().GetResult();

        return rows.Select(RowToBucketTag).ToList();
    }

    public List<ObjectTag> GetObjectTags(Bucket bucket, string key, long version, S3Logger log)
    {
        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("GetTags unable to find key " + bucket.Name + "/" + key + " version " + version);
            return null;
        }

        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}objecttags WHERE bucketguid = @bg AND objectguid = @og",
            new[]
            {
                SqlParameter.Named("bg", SqlValue.From(bucket.GUID)),
                SqlParameter.Named("og", SqlValue.From(obj.GUID)),
            }).GetAwaiter().GetResult();

        return rows.Select(RowToObjectTag).ToList();
    }

    public List<ObjectTag> GetObjectTags(Bucket bucket, string guid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}objecttags WHERE bucketguid = @bg AND objectguid = @og",
            new[]
            {
                SqlParameter.Named("bg", SqlValue.From(bucket.GUID)),
                SqlParameter.Named("og", SqlValue.From(guid)),
            }).GetAwaiter().GetResult();

        return rows.Select(RowToObjectTag).ToList();
    }

    public void DeleteBucketTags(Bucket bucket)
    {
        _db.ExecuteAsync(
            $"DELETE FROM {_t}buckettags WHERE bucketguid = @bg",
            new[] { SqlParameter.Named("bg", SqlValue.From(bucket.GUID)) })
            .GetAwaiter().GetResult();
    }

    public void DeleteObjectVersionTags(Bucket bucket, string key, long version, S3Logger log)
    {
        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("DeleteTags unable to find key " + bucket.Name + "/" + key + " version " + version);
            return;
        }

        _db.ExecuteAsync(
            $"DELETE FROM {_t}objecttags WHERE bucketguid = @bg AND objectguid = @og",
            new[]
            {
                SqlParameter.Named("bg", SqlValue.From(bucket.GUID)),
                SqlParameter.Named("og", SqlValue.From(obj.GUID)),
            }).GetAwaiter().GetResult();
    }

    // -------------------------------------------------------------------------
    // ACLs
    // -------------------------------------------------------------------------

    public bool ObjectGroupAclExists(Bucket bucket, string groupName, string key, long version, S3Logger log)
    {
        if (string.IsNullOrEmpty(groupName)) throw new ArgumentNullException(nameof(groupName));
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("Exists unable to find key " + bucket.Name + "/" + key + " version " + version);
            return false;
        }

        var rows = _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt FROM {_t}objectacls WHERE bucketguid = @bg AND objectguid = @og AND usergroup = @grp",
            new[]
            {
                SqlParameter.Named("bg",  SqlValue.From(bucket.GUID)),
                SqlParameter.Named("og",  SqlValue.From(obj.GUID)),
                SqlParameter.Named("grp", SqlValue.From(groupName)),
            }).GetAwaiter().GetResult();

        return rows.Count > 0 && rows[0]["cnt"].AsInt() > 0;
    }

    public bool ObjectUserAclExists(Bucket bucket, string userGuid, string key, long version, S3Logger log)
    {
        if (string.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("Exists unable to find key " + bucket.Name + "/" + key + " version " + version);
            return false;
        }

        var rows = _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt FROM {_t}objectacls WHERE bucketguid = @bg AND objectguid = @og AND userguid = @ug",
            new[]
            {
                SqlParameter.Named("bg", SqlValue.From(bucket.GUID)),
                SqlParameter.Named("og", SqlValue.From(obj.GUID)),
                SqlParameter.Named("ug", SqlValue.From(userGuid)),
            }).GetAwaiter().GetResult();

        return rows.Count > 0 && rows[0]["cnt"].AsInt() > 0;
    }

    public bool BucketGroupAclExists(Bucket bucket, string groupName)
    {
        var rows = _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt FROM {_t}bucketacls WHERE bucketguid = @bg AND usergroup = @grp",
            new[]
            {
                SqlParameter.Named("bg",  SqlValue.From(bucket.GUID)),
                SqlParameter.Named("grp", SqlValue.From(groupName)),
            }).GetAwaiter().GetResult();

        return rows.Count > 0 && rows[0]["cnt"].AsInt() > 0;
    }

    public bool BucketUserAclExists(Bucket bucket, string userGuid)
    {
        var rows = _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt FROM {_t}bucketacls WHERE bucketguid = @bg AND userguid = @ug",
            new[]
            {
                SqlParameter.Named("bg", SqlValue.From(bucket.GUID)),
                SqlParameter.Named("ug", SqlValue.From(userGuid)),
            }).GetAwaiter().GetResult();

        return rows.Count > 0 && rows[0]["cnt"].AsInt() > 0;
    }

    public List<BucketAcl> GetBucketAcl(Bucket bucket)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}bucketacls WHERE bucketguid = @bg",
            new[] { SqlParameter.Named("bg", SqlValue.From(bucket.GUID)) })
            .GetAwaiter().GetResult();

        return rows.Select(RowToBucketAcl).ToList();
    }

    public List<ObjectAcl> GetObjectVersionAcl(Bucket bucket, string key, long version, S3Logger log)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("GetAcl unable to find key " + bucket.Name + "/" + key + " version " + version);
            return null;
        }

        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}objectacls WHERE bucketguid = @bg AND objectguid = @og",
            new[]
            {
                SqlParameter.Named("bg", SqlValue.From(bucket.GUID)),
                SqlParameter.Named("og", SqlValue.From(obj.GUID)),
            }).GetAwaiter().GetResult();

        return rows.Select(RowToObjectAcl).ToList();
    }

    public List<ObjectAcl> GetObjectAcl(Bucket bucket, string guid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}objectacls WHERE bucketguid = @bg AND objectguid = @og",
            new[]
            {
                SqlParameter.Named("bg", SqlValue.From(bucket.GUID)),
                SqlParameter.Named("og", SqlValue.From(guid)),
            }).GetAwaiter().GetResult();

        return rows.Select(RowToObjectAcl).ToList();
    }

    public void SetBucketAcls(Bucket bucket, List<BucketAcl> acls)
    {
        DeleteBucketAcl(bucket);
        if (acls == null || acls.Count == 0) return;
        foreach (var acl in acls)
        {
            acl.BucketGUID = bucket.GUID;
            InsertBucketAcl(acl);
        }
    }

    public void SetObjectAcls(Bucket bucket, string key, long version, List<ObjectAcl> acls, S3Logger log)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("SetAcl unable to find key " + bucket.Name + "/" + key + " version " + version);
            return;
        }

        DeleteObjectVersionAcl(bucket, key, version, log);

        if (acls == null || acls.Count == 0) return;
        foreach (var acl in acls)
        {
            acl.BucketGUID = bucket.GUID;
            acl.ObjectGUID = obj.GUID;
            InsertObjectAcl(acl);
        }
    }

    public void DeleteBucketAcl(Bucket bucket)
    {
        _db.ExecuteAsync(
            $"DELETE FROM {_t}bucketacls WHERE bucketguid = @bg",
            new[] { SqlParameter.Named("bg", SqlValue.From(bucket.GUID)) })
            .GetAwaiter().GetResult();
    }

    public void DeleteObjectVersionAcl(Bucket bucket, string key, long version, S3Logger log)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        var obj = GetObjectVersionMetadata(bucket, key, version);
        if (obj == null)
        {
            log.Debug("DeleteAcl unable to find key " + bucket.Name + "/" + key + " version " + version);
            return;
        }

        _db.ExecuteAsync(
            $"DELETE FROM {_t}objectacls WHERE bucketguid = @bg AND objectguid = @og",
            new[]
            {
                SqlParameter.Named("bg", SqlValue.From(bucket.GUID)),
                SqlParameter.Named("og", SqlValue.From(obj.GUID)),
            }).GetAwaiter().GetResult();
    }

    public void AddBucketAcl(Bucket bucket, BucketAcl acl)
    {
        if (acl == null) return;
        acl.BucketGUID = bucket.GUID;
        InsertBucketAcl(acl);
    }

    public void AddObjectAcl(Bucket bucket, ObjectAcl acl, S3Logger log)
    {
        if (acl == null) return;
        acl.BucketGUID = bucket.GUID;
        InsertObjectAcl(acl);
    }

    public void DeleteObjectAcl(Bucket bucket, string key, S3Logger log)
    {
        var obj = GetObjectLatestMetadata(bucket, key);
        if (obj == null)
        {
            log?.Debug("DeleteObjectAcl unable to find key " + bucket.Name + "/" + key);
            return;
        }

        _db.ExecuteAsync(
            $"DELETE FROM {_t}objectacls WHERE bucketguid = @bg AND objectguid = @og",
            new[]
            {
                SqlParameter.Named("bg", SqlValue.From(bucket.GUID)),
                SqlParameter.Named("og", SqlValue.From(obj.GUID)),
            }).GetAwaiter().GetResult();
    }

    private void InsertBucketAcl(BucketAcl acl)
    {
        _db.ExecuteAsync(
            $"INSERT INTO {_t}bucketacls (guid, usergroup, bucketguid, userguid, issuedbyuserguid, permitread, permitwrite, permitreadacp, permitwriteacp, permitfullcontrol, createdutc) " +
            "VALUES (@guid, @ug, @bg, @userguid, @issued, @pr, @pw, @pracp, @pwacp, @fc, @ts)",
            new[]
            {
                SqlParameter.Named("guid",   SqlValue.From(acl.GUID)),
                SqlParameter.Named("ug",     acl.UserGroup != null ? SqlValue.From(acl.UserGroup) : SqlValue.Null_),
                SqlParameter.Named("bg",     SqlValue.From(acl.BucketGUID)),
                SqlParameter.Named("userguid", acl.UserGUID != null ? SqlValue.From(acl.UserGUID) : SqlValue.Null_),
                SqlParameter.Named("issued", acl.IssuedByUserGUID != null ? SqlValue.From(acl.IssuedByUserGUID) : SqlValue.Null_),
                SqlParameter.Named("pr",     SqlValue.From(acl.PermitRead ? 1 : 0)),
                SqlParameter.Named("pw",     SqlValue.From(acl.PermitWrite ? 1 : 0)),
                SqlParameter.Named("pracp",  SqlValue.From(acl.PermitReadAcp ? 1 : 0)),
                SqlParameter.Named("pwacp",  SqlValue.From(acl.PermitWriteAcp ? 1 : 0)),
                SqlParameter.Named("fc",     SqlValue.From(acl.FullControl ? 1 : 0)),
                SqlParameter.Named("ts",     SqlValue.From(acl.CreatedUtc)),
            }).GetAwaiter().GetResult();
    }

    private void InsertObjectAcl(ObjectAcl acl)
    {
        _db.ExecuteAsync(
            $"INSERT INTO {_t}objectacls (guid, usergroup, userguid, issuedbyuserguid, bucketguid, objectguid, permitread, permitwrite, permitreadacp, permitwriteacp, permitfullcontrol, createdutc) " +
            "VALUES (@guid, @ug, @userguid, @issued, @bg, @og, @pr, @pw, @pracp, @pwacp, @fc, @ts)",
            new[]
            {
                SqlParameter.Named("guid",     SqlValue.From(acl.GUID)),
                SqlParameter.Named("ug",       acl.UserGroup != null ? SqlValue.From(acl.UserGroup) : SqlValue.Null_),
                SqlParameter.Named("userguid", acl.UserGUID != null ? SqlValue.From(acl.UserGUID) : SqlValue.Null_),
                SqlParameter.Named("issued",   acl.IssuedByUserGUID != null ? SqlValue.From(acl.IssuedByUserGUID) : SqlValue.Null_),
                SqlParameter.Named("bg",       SqlValue.From(acl.BucketGUID)),
                SqlParameter.Named("og",       SqlValue.From(acl.ObjectGUID)),
                SqlParameter.Named("pr",       SqlValue.From(acl.PermitRead ? 1 : 0)),
                SqlParameter.Named("pw",       SqlValue.From(acl.PermitWrite ? 1 : 0)),
                SqlParameter.Named("pracp",    SqlValue.From(acl.PermitReadAcp ? 1 : 0)),
                SqlParameter.Named("pwacp",    SqlValue.From(acl.PermitWriteAcp ? 1 : 0)),
                SqlParameter.Named("fc",       SqlValue.From(acl.FullControl ? 1 : 0)),
                SqlParameter.Named("ts",       SqlValue.From(acl.CreatedUtc)),
            }).GetAwaiter().GetResult();
    }

    // -------------------------------------------------------------------------
    // Users
    // -------------------------------------------------------------------------

    public List<User> GetUsers()
    {
        var rows = _db.QueryAsync($"SELECT * FROM {_t}users ORDER BY name")
            .GetAwaiter().GetResult();
        return rows.Select(RowToUser).ToList();
    }

    public bool UserGuidExists(string guid)
    {
        var rows = _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt FROM {_t}users WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 && rows[0]["cnt"].AsInt() > 0;
    }

    public bool UserEmailExists(string email)
    {
        var rows = _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt FROM {_t}users WHERE email = @email",
            new[] { SqlParameter.Named("email", SqlValue.From(email)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 && rows[0]["cnt"].AsInt() > 0;
    }

    public User? GetUserByGuid(string guid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}users WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 ? RowToUser(rows[0]) : null;
    }

    public User? GetUserByName(string name)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}users WHERE name = @name",
            new[] { SqlParameter.Named("name", SqlValue.From(name)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 ? RowToUser(rows[0]) : null;
    }

    public User? GetUserByEmail(string email)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}users WHERE email = @email",
            new[] { SqlParameter.Named("email", SqlValue.From(email)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 ? RowToUser(rows[0]) : null;
    }

    public User AddUser(User user)
    {
        _db.ExecuteAsync(
            $"INSERT INTO {_t}users (guid, name, email, createdutc) VALUES (@guid, @name, @email, @ts)",
            new[]
            {
                SqlParameter.Named("guid",  SqlValue.From(user.GUID)),
                SqlParameter.Named("name",  SqlValue.From(user.Name)),
                SqlParameter.Named("email", SqlValue.From(user.Email)),
                SqlParameter.Named("ts",    SqlValue.From(user.CreatedUtc)),
            }).GetAwaiter().GetResult();
        return user;
    }

    public void DeleteUser(string guid)
    {
        _db.ExecuteAsync(
            $"DELETE FROM {_t}users WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
    }

    // -------------------------------------------------------------------------
    // Credentials
    // -------------------------------------------------------------------------

    public List<Credential> GetCredentials()
    {
        var rows = _db.QueryAsync($"SELECT * FROM {_t}credentials")
            .GetAwaiter().GetResult();
        return rows.Select(RowToCredential).ToList();
    }

    public bool CredentialGuidExists(string guid)
    {
        var rows = _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt FROM {_t}credentials WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 && rows[0]["cnt"].AsInt() > 0;
    }

    public Credential? GetCredentialByGuid(string guid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}credentials WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 ? RowToCredential(rows[0]) : null;
    }

    public List<Credential> GetCredentialsByUser(string userGuid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}credentials WHERE userguid = @ug",
            new[] { SqlParameter.Named("ug", SqlValue.From(userGuid)) })
            .GetAwaiter().GetResult();
        return rows.Select(RowToCredential).ToList();
    }

    public Credential? GetCredentialByAccessKey(string accessKey)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}credentials WHERE accesskey = @ak",
            new[] { SqlParameter.Named("ak", SqlValue.From(accessKey)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 ? RowToCredential(rows[0]) : null;
    }

    public Credential AddCredential(Credential cred)
    {
        _db.ExecuteAsync(
            $"INSERT INTO {_t}credentials (guid, userguid, description, accesskey, secretkey, isbase64, createdutc) " +
            "VALUES (@guid, @ug, @desc, @ak, @sk, @b64, @ts)",
            new[]
            {
                SqlParameter.Named("guid", SqlValue.From(cred.GUID)),
                SqlParameter.Named("ug",   SqlValue.From(cred.UserGUID)),
                SqlParameter.Named("desc", cred.Description != null ? SqlValue.From(cred.Description) : SqlValue.Null_),
                SqlParameter.Named("ak",   SqlValue.From(cred.AccessKey)),
                SqlParameter.Named("sk",   SqlValue.From(cred.SecretKey)),
                SqlParameter.Named("b64",  SqlValue.From(cred.IsBase64 ? 1 : 0)),
                SqlParameter.Named("ts",   SqlValue.From(cred.CreatedUtc)),
            }).GetAwaiter().GetResult();
        return cred;
    }

    public void DeleteCredential(string guid)
    {
        _db.ExecuteAsync(
            $"DELETE FROM {_t}credentials WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
    }

    // -------------------------------------------------------------------------
    // Buckets
    // -------------------------------------------------------------------------

    public List<Bucket> GetBuckets()
    {
        var rows = _db.QueryAsync($"SELECT * FROM {_t}buckets ORDER BY name")
            .GetAwaiter().GetResult();
        return rows.Select(RowToBucket).ToList();
    }

    public bool BucketExists(string name)
    {
        var rows = _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt FROM {_t}buckets WHERE name = @name",
            new[] { SqlParameter.Named("name", SqlValue.From(name)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 && rows[0]["cnt"].AsInt() > 0;
    }

    public List<Bucket> GetBucketsByUser(string userGuid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}buckets WHERE ownerguid = @ug ORDER BY name",
            new[] { SqlParameter.Named("ug", SqlValue.From(userGuid)) })
            .GetAwaiter().GetResult();
        return rows.Select(RowToBucket).ToList();
    }

    public Bucket? GetBucketByGuid(string guid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}buckets WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 ? RowToBucket(rows[0]) : null;
    }

    public Bucket? GetBucketByName(string name)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}buckets WHERE name = @name",
            new[] { SqlParameter.Named("name", SqlValue.From(name)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 ? RowToBucket(rows[0]) : null;
    }

    public Bucket AddBucket(Bucket bucket)
    {
        _db.ExecuteAsync(
            $"INSERT INTO {_t}buckets (guid, ownerguid, name, regionstring, storagetype, diskdirectory, enableversioning, enablepublicwrite, enablepublicread, createdutc) " +
            "VALUES (@guid, @owner, @name, @region, @st, @dir, @ev, @epw, @epr, @ts)",
            new[]
            {
                SqlParameter.Named("guid",   SqlValue.From(bucket.GUID)),
                SqlParameter.Named("owner",  SqlValue.From(bucket.OwnerGUID)),
                SqlParameter.Named("name",   SqlValue.From(bucket.Name)),
                SqlParameter.Named("region", SqlValue.From(bucket.RegionString)),
                SqlParameter.Named("st",     SqlValue.From(bucket.StorageType.ToString())),
                SqlParameter.Named("dir",    SqlValue.From(bucket.DiskDirectory)),
                SqlParameter.Named("ev",     SqlValue.From(bucket.EnableVersioning ? 1 : 0)),
                SqlParameter.Named("epw",    SqlValue.From(bucket.EnablePublicWrite ? 1 : 0)),
                SqlParameter.Named("epr",    SqlValue.From(bucket.EnablePublicRead ? 1 : 0)),
                SqlParameter.Named("ts",     SqlValue.From(bucket.CreatedUtc)),
            }).GetAwaiter().GetResult();
        return bucket;
    }

    public void DeleteBucket(string guid)
    {
        _db.ExecuteAsync(
            $"DELETE FROM {_t}buckets WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
    }

    // -------------------------------------------------------------------------
    // Multipart Uploads
    // -------------------------------------------------------------------------

    public List<Upload> GetUploads()
    {
        var rows = _db.QueryAsync($"SELECT * FROM {_t}uploads ORDER BY createdutc")
            .GetAwaiter().GetResult();
        return rows.Select(RowToUpload).ToList();
    }

    public List<Upload> GetUploadsByBucketGuid(string bucketGuid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}uploads WHERE bucketguid = @bg ORDER BY createdutc",
            new[] { SqlParameter.Named("bg", SqlValue.From(bucketGuid)) })
            .GetAwaiter().GetResult();
        return rows.Select(RowToUpload).ToList();
    }

    public Upload? GetUploadByGuid(string guid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}uploads WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
        return rows.Count > 0 ? RowToUpload(rows[0]) : null;
    }

    public Upload AddUpload(Upload upload)
    {
        _db.ExecuteAsync(
            $"INSERT INTO {_t}uploads (guid, bucketguid, ownerguid, authorguid, objectkey, createdutc, lastaccessutc, expirationutc, contenttype, metadata) " +
            "VALUES (@guid, @bg, @owner, @author, @objectkey, @created, @accessed, @expires, @ct, @meta)",
            new[]
            {
                SqlParameter.Named("guid",      SqlValue.From(upload.GUID)),
                SqlParameter.Named("bg",        SqlValue.From(upload.BucketGUID)),
                SqlParameter.Named("owner",     SqlValue.From(upload.OwnerGUID)),
                SqlParameter.Named("author",    SqlValue.From(upload.AuthorGUID)),
                SqlParameter.Named("objectkey", SqlValue.From(upload.Key)),
                SqlParameter.Named("created",  SqlValue.From(upload.CreatedUtc)),
                SqlParameter.Named("accessed", SqlValue.From(upload.LastAccessUtc)),
                SqlParameter.Named("expires",  SqlValue.From(upload.ExpirationUtc)),
                SqlParameter.Named("ct",       upload.ContentType != null ? SqlValue.From(upload.ContentType) : SqlValue.Null_),
                SqlParameter.Named("meta",     upload.Metadata != null ? SqlValue.From(upload.Metadata) : SqlValue.Null_),
            }).GetAwaiter().GetResult();
        return upload;
    }

    public void DeleteUpload(string guid)
    {
        _db.ExecuteAsync(
            $"DELETE FROM {_t}uploads WHERE guid = @guid",
            new[] { SqlParameter.Named("guid", SqlValue.From(guid)) })
            .GetAwaiter().GetResult();
    }

    public void AddUploadPart(UploadPart part)
    {
        _db.ExecuteAsync(
            $"INSERT INTO {_t}uploadparts (guid, bucketguid, ownerguid, uploadguid, partnumber, partlength, md5hash, sha1hash, sha256hash, lastaccessutc, createdutc) " +
            "VALUES (@guid, @bg, @owner, @ug, @pnum, @plen, @md5, @sha1, @sha256, @accessed, @created)",
            new[]
            {
                SqlParameter.Named("guid",     SqlValue.From(part.GUID)),
                SqlParameter.Named("bg",       SqlValue.From(part.BucketGUID)),
                SqlParameter.Named("owner",    SqlValue.From(part.OwnerGUID)),
                SqlParameter.Named("ug",       SqlValue.From(part.UploadGUID)),
                SqlParameter.Named("pnum",     SqlValue.From(part.PartNumber)),
                SqlParameter.Named("plen",     SqlValue.From(part.PartLength)),
                SqlParameter.Named("md5",      SqlValue.From(part.MD5Hash)),
                SqlParameter.Named("sha1",     SqlValue.From(part.Sha1Hash)),
                SqlParameter.Named("sha256",   SqlValue.From(part.Sha256Hash)),
                SqlParameter.Named("accessed", SqlValue.From(part.LastAccessUtc)),
                SqlParameter.Named("created",  SqlValue.From(part.CreatedUtc)),
            }).GetAwaiter().GetResult();
    }

    public List<UploadPart> GetUploadPartsByGuid(string uploadGuid)
    {
        var rows = _db.QueryAsync(
            $"SELECT * FROM {_t}uploadparts WHERE uploadguid = @ug ORDER BY partnumber",
            new[] { SqlParameter.Named("ug", SqlValue.From(uploadGuid)) })
            .GetAwaiter().GetResult();
        return rows.Select(RowToUploadPart).ToList();
    }

    public void DeleteUploadParts(string uploadGuid)
    {
        _db.ExecuteAsync(
            $"DELETE FROM {_t}uploadparts WHERE uploadguid = @ug",
            new[] { SqlParameter.Named("ug", SqlValue.From(uploadGuid)) })
            .GetAwaiter().GetResult();
    }
}
