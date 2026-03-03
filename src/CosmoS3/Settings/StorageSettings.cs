namespace CosmoS3.Settings
{
    using CosmoS3.Storage;

    /// <summary>
    /// Storage settings.
    /// </summary>
    public class StorageSettings
    {
        /// <summary>
        /// Temporary storage directory.
        /// </summary>
        public string TempDirectory { get; set; } = "./temp/";

        /// <summary>
        /// Type of storage driver.
        /// </summary>
        public StorageDriverType StorageType { get; set; } = StorageDriverType.Disk;

        /// <summary>
        /// Storage directory for 'Disk' StorageType.
        /// </summary>
        public string DiskDirectory { get; set; } = "./disk/";

        /// <summary>
        /// Storage settings.
        /// </summary>
        public StorageSettings()
        {

        }
    }
}
