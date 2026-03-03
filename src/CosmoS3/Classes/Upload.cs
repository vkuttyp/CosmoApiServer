using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CosmoS3.Classes;

public class Upload
{
    public int Id { get; set; }
    public string GUID { get; set; } = Guid.NewGuid().ToString();
    public string BucketGUID { get; set; } = "";
    public string OwnerGUID { get; set; }= "";
    public string AuthorGUID { get; set; } = "";
    public string Key { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpirationUtc { get; set; } = DateTime.UtcNow.AddSeconds(60 * 60 * 24 * 7); // seven days
    public string ContentType { get; set; } = null;
    public string Metadata { get; set; } = null;
}