using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CosmoS3.Classes;

public class UploadPart
{
    public int Id { get; set; }
    public string GUID { get; set; }=Guid.NewGuid().ToString();
    public string BucketGUID { get; set; }= Guid.NewGuid().ToString();
    public string OwnerGUID { get; set; } = Guid.NewGuid().ToString();
    public string UploadGUID { get; set; } = Guid.NewGuid().ToString();
    public int PartNumber { get; set; }
    public int PartLength { get; set; }
    public string MD5Hash { get; set; } = "";
    public string Sha1Hash { get; set; } = "";
    public string Sha256Hash { get; set; } = "";
    public DateTime LastAccessUtc { get; set; }= DateTime.UtcNow;
    public DateTime CreatedUtc { get; set; }= DateTime.UtcNow;

}