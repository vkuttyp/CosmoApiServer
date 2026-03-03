using System.Text.Json.Serialization;
using CosmoS3.Storage;

namespace CosmoS3.Classes
{
    /// <summary>
    /// Bucket configuration.
    /// </summary>
    public class Bucket
    {
        public int Id { get; set; } = 0;
        public string GUID { get; set; } = GuidSortable.NewGuid().ToString();
        public string OwnerGUID { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = null;
        public string RegionString { get; set; } = "us-west-1";
        public StorageDriverType StorageType { get; set; } = StorageDriverType.Disk;
        public string DiskDirectory { get; set; } = "./disk/";
        public bool EnableVersioning { get; set; } = false;
        public bool EnablePublicWrite { get; set; } = false;
        public bool EnablePublicRead { get; set; } = false;
        public DateTime CreatedUtc { get; set; } = DateTime.Now.ToUniversalTime();

        public Bucket()
        {

        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="owner">Owner GUID.</param>
        /// <param name="storageType">Storage type.</param>
        /// <param name="diskDirectory">Disk directory.</param>
        /// <param name="region">Region.</param>
        public Bucket(
            string name,
            string owner,
            StorageDriverType storageType,
            string diskDirectory,
            string region = "us-west-1")
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(owner)) throw new ArgumentNullException(nameof(owner));
            if (String.IsNullOrEmpty(diskDirectory)) throw new ArgumentNullException(nameof(diskDirectory));

            Name = name;
            RegionString = region;
            StorageType = storageType;
            DiskDirectory = diskDirectory;
            OwnerGUID = owner;
            CreatedUtc = DateTime.Now.ToUniversalTime();
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="guid">GUID.</param>
        /// <param name="name">Name.</param>
        /// <param name="owner">Owner GUID.</param>
        /// <param name="storageType">Storage type.</param>
        /// <param name="diskDirectory">Disk directory.</param>
        /// <param name="region">Region.</param>
        public Bucket(
            string guid,
            string name,
            string owner,
            StorageDriverType storageType,
            string diskDirectory,
            string region = "us-west-1")
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(owner)) throw new ArgumentNullException(nameof(owner));
            if (String.IsNullOrEmpty(diskDirectory)) throw new ArgumentNullException(nameof(diskDirectory));

            GUID = guid;
            Name = name;
            RegionString = region;
            StorageType = storageType;
            DiskDirectory = diskDirectory;
            OwnerGUID = owner;
            CreatedUtc = DateTime.Now.ToUniversalTime();
        }


    }
}
