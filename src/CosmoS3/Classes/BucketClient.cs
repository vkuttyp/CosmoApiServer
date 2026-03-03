
using CosmoS3.Settings;
using CosmoS3.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using CosmoS3.Logging;
namespace CosmoS3.Classes
{
    /// <summary>
    /// Bucket client.  All object construction, authentication, and authorization must occur prior to using bucket methods.
    /// </summary>
    internal class BucketClient : IDisposable
    {
        #region Internal-Members

        internal long StreamReadBufferSize
        {
            get
            {
                return _StreamReadBufferSize;
            }
            set
            {
                if (value < 1) throw new ArgumentException("StreamReadBufferSize must be greater than zero.");
                _StreamReadBufferSize = value;
            }
        }

        internal string Name
        {
            get
            {
                return _Bucket.Name;
            } 
        }

        internal string GUID
        {
            get
            {
                return _Bucket.GUID;
            }
        }

        #endregion

        #region Private-Members

        private SettingsBase _Settings = null;
        private S3Logger _Logging = null;
        private Bucket _Bucket = null;
        private long _StreamReadBufferSize = 65536;
        private StorageDriverBase _StorageDriver = null;

        #endregion

        #region Constructors-and-Factories

        internal BucketClient()
        {

        }

        internal BucketClient(SettingsBase settings, S3Logger logging, Bucket bucket)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
             
            InitializeStorageDriver(); 
        }

        #endregion

        #region Public-Methods

        public void Dispose()
        {
            if (_StorageDriver != null)
            { 
                _StorageDriver = null;
            }
        }

        #endregion

        #region Internal-Methods

        internal bool AddObject(Obj obj, byte[] data)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
             
            long len = 0;
            MemoryStream ms = new MemoryStream();

            if (data != null && data.Length > 0)
            { 
                len = data.Length;
                ms.Write(data, 0, data.Length);
                ms.Seek(0, SeekOrigin.Begin);
            }
             
            obj.ContentLength = len;
            return AddObject(obj, ms);
        }

        internal bool AddObject(Obj obj, Stream stream)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (String.IsNullOrEmpty(obj.GUID)) obj.GUID = GuidSortable.NewGuid().ToString();
            obj.BucketGUID = _Bucket.GUID;

            var test = GetObjectLatestMetadata(obj.Key);
            if (test != null)
            {
                if (!_Bucket.EnableVersioning)
                {
                    // Overwrite: delete the old blob and DB record, then write new
                    _StorageDriver.Delete(test.BlobFilename);
                    DataAccess.DeleteObjectRecord(test.GUID);
                    obj.Version = 1;
                }
                else
                {
                    obj.Version = (test.Version + 1);
                }
            }
            else
            {
                obj.Version = 1;
            }

            obj.Md5 = Convert.ToHexString(_StorageDriver.Write(obj.BlobFilename, obj.ContentLength, stream)).ToLowerInvariant();

            if (String.IsNullOrEmpty(obj.Etag)) obj.Etag = obj.Md5;

            DateTime ts = DateTime.Now.ToUniversalTime();
            obj.CreatedUtc = ts;
            obj.LastAccessUtc = ts;
            obj.LastUpdateUtc = ts;
            obj.ExpirationUtc = null;
            return DataAccess.SaveObject(_Bucket,obj, _Logging);
        }

        internal bool AddObjectMetadata(Obj obj)
        {
            return DataAccess.SaveObject(_Bucket,obj, _Logging);
        }

        internal bool GetObjectLatest(string key, out byte[] data)
        {
            data = null;
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            var obj = GetObjectLatestMetadata(key);
            if (obj == null) return false;

            data = _StorageDriver.Read(obj.BlobFilename);
            return true;
        }
         
        internal bool GetObjectLatest(string key, out long contentLength, out Stream stream)
        {
            contentLength = 0;
            stream = null;
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            var obj = GetObjectLatestMetadata(key);
            if (obj == null) return false;

            ObjectStream objStream = _StorageDriver.ReadStream(obj.BlobFilename);
            contentLength = objStream.ContentLength;
            stream = objStream.Data;
            return true;
        }

        internal bool GetObjectLatestRange(string key, long startPosition, long length, out Stream stream)
        {
            stream = null;
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (startPosition < 0) throw new ArgumentNullException(nameof(startPosition));
            if (length < 0) throw new ArgumentNullException(nameof(length));

            var obj = GetObjectLatestMetadata(key);
            if (obj == null) return false;

            ObjectStream objStream = _StorageDriver.ReadRangeStream(obj.BlobFilename, startPosition, length);
            stream = objStream.Data;
            return true; 
        }

        internal long GetObjectLatestVersion(string key)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            return DataAccess.GetObjectLatestVersion(key);
        }

        internal BucketStatistics GetFullStatistics()
        {
            return DataAccess.GetStatics(_Bucket);
        }

        internal BucketStatistics GetStatistics(List<Obj> objects)
        {
            BucketStatistics ret = new BucketStatistics(_Bucket.Name, _Bucket.GUID, 0, 0);
            
            if (objects != null && objects.Count > 0)
            {
                ret.Objects = objects.Count;
                ret.Bytes = objects.Sum(o => o.ContentLength);
            }

            return ret;
        }

        internal Obj GetObjectLatestMetadata(string key)
        { 
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            return DataAccess.GetObjectLatestMetadata(_Bucket, key);
        }

        internal Obj GetObjectVersionMetadata(string key, long version = 1)
        { 
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            return DataAccess.GetObjectVersionMetadata(_Bucket, key, version);
        }

        internal Obj GetObjectMetadataByGuid(string guid)
        { 
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));

           return DataAccess.GetObjectMetadataByGuid(_Bucket, guid);
        }

        internal bool ObjectExists(string key)
        { 
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            var obj = GetObjectLatestMetadata(key);
            if (obj != null) return true;
            return false;
        }

        internal bool ObjectVersionExists(string key, long version)
        { 
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            var obj = GetObjectVersionMetadata(key, version);
            if (obj != null) return true;
            return false;
        }

        internal bool DeleteLatestObject(string key )
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            return DataAccess.DeleteLatestObject(_Bucket, key, _Logging, _StorageDriver);
        }

        internal bool DeleteObjectVersion(string key, long version)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            return DataAccess.DeleteObjectVersion(_Bucket,key, version, _Logging, _StorageDriver);
        }

        internal bool DeleteObjectVersionMetadata(string key, long version)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            return DataAccess.DeleteObjectVersionMetadata(_Bucket, key, version, _Logging, _StorageDriver);
        }

        internal void Enumerate(
            string delimiter,
            string prefix,
            int startIndex,
            int maxResults,
            out List<Obj> objects,
            out List<string> prefixes,
            out int nextStartIndex,
            out bool isTruncated)
        {
            DataAccess.Enumerate(_Bucket, delimiter, prefix, startIndex, maxResults, out objects, out prefixes, out nextStartIndex, out isTruncated);
        }

        internal void AddBucketTags(List<BucketTag> tags)
        {
            DeleteBucketTags();

            DataAccess.AddBucketTags(_Bucket, tags);
        }

        internal void AddObjectVersionTags(string key, long version, List<ObjectTag> tags)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (version < 1) throw new ArgumentException("Version ID must be one or greater.");

            DataAccess.AddObjectVersionTags(_Bucket, key, version, tags, _Logging);
        }

        internal List<BucketTag> GetBucketTags()
        {
            return DataAccess.GetBucketTags(_Bucket);
        }

        internal List<ObjectTag> GetObjectTags(string key, long version)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (version < 1) throw new ArgumentException("Version ID must be one or greater.");

            return DataAccess.GetObjectTags(_Bucket, key, version, _Logging);
        }

        internal List<ObjectTag> GetObjectTags(string guid)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid)); 
             
            return DataAccess.GetObjectTags(_Bucket, guid);
        }

        internal void DeleteBucketTags()
        {
           DataAccess.DeleteBucketTags(_Bucket);
        }

        internal void DeleteObjectVersionTags(string key, long version)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            DataAccess.DeleteObjectVersionTags(_Bucket, key, version, _Logging);
        }

        internal bool ObjectGroupAclExists(string groupName, string key, long version)
        {
            return DataAccess.ObjectGroupAclExists(_Bucket, groupName, key, version, _Logging);
        }

        internal bool ObjectUserAclExists(string userGuid, string key, long version)
        {

            return DataAccess.ObjectUserAclExists(_Bucket, userGuid, key, version, _Logging);
        }

        internal bool BucketGroupAclExists(string groupName)
        {
            if (String.IsNullOrEmpty(groupName)) throw new ArgumentNullException(nameof(groupName)); 
             
           return DataAccess.BucketGroupAclExists(_Bucket,groupName);
        }

        internal bool BucketUserAclExists(string userGuid)
        {
            if (String.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));

           return DataAccess.BucketUserAclExists(_Bucket, userGuid);
        }

        internal List<BucketAcl> GetBucketAcl()
        {
            return DataAccess.GetBucketAcl(_Bucket);
        }

        internal List<ObjectAcl> GetObjectVersionAcl(string key, long version)
        {

            return DataAccess.GetObjectVersionAcl(_Bucket, key, version, _Logging);
        }

        internal List<ObjectAcl> GetObjectAcl(string guid)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));
              
           return DataAccess.GetObjectAcl(_Bucket, guid);
        }

        internal void AddBucketAcl(BucketAcl acl)
        {
            DataAccess.AddBucketAcl(_Bucket,acl);
        }

        internal void SetBucketAcls(List<BucketAcl> acls)
        {
            DeleteBucketAcl();

            DataAccess.SetBucketAcls(_Bucket, acls);
        }

        internal void AddObjectAcl(ObjectAcl acl)
        {
            
            DataAccess.AddObjectAcl(_Bucket, acl, _Logging);

        }

        internal void SetObjectAcls(string key, long version, List<ObjectAcl> acls)
        {

            DataAccess.SetObjectAcls(_Bucket, key, version, acls, _Logging);
        }

        internal void DeleteBucketAcl()
        {
           DataAccess.DeleteBucketAcl(_Bucket);
        }

        internal void DeleteObjectVersionAcl(string key, long version)
        {

           DataAccess.DeleteObjectVersionAcl(_Bucket, key, version,_Logging);
        }

        internal void DeleteObjectAcl(string key)
        {

           DataAccess.DeleteObjectAcl(_Bucket, key, _Logging);
        }

        #endregion

        #region Private-Methods
          
        private void InitializeStorageDriver()
        {
            switch (_Bucket.StorageType)
            {
                case StorageDriverType.Disk:
                    if (!Directory.Exists(_Bucket.DiskDirectory)) Directory.CreateDirectory(_Bucket.DiskDirectory);
                    _StorageDriver = new DiskStorageDriver(_Bucket.DiskDirectory);
                    break;

                default:
                    throw new ArgumentException("Unknown storage driver type '" + _Bucket.StorageType.ToString() + "' in bucket GUID " + _Bucket.GUID + ".");
            }
        }
         
        private void Logger(string msg)
        {
            Console.WriteLine(msg);
        }

        #endregion
    }
}
