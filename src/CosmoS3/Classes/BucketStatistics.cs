using System;
using System.Collections.Generic;
using System.Text;

namespace CosmoS3.Classes
{
    /// <summary>
    /// Bucket statistics.
    /// </summary>
    public class BucketStatistics
    {
        
        public string Name { get; set; } = null;
        public string GUID { get; set; } = Guid.NewGuid().ToString();
        public long Objects = 0;
        public long Bytes = 0;
        public BucketStatistics()
        {

        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="guid">GUID.</param>
        /// <param name="objects">Number of objects.</param>
        /// <param name="bytes">Number of bytes.</param>
        public BucketStatistics(string name, string guid, long objects, long bytes)
        {
            this.Name = name;
            this.GUID = guid;
            this.Objects = objects;
            this.Bytes = bytes;
        }
    }
}
