using MurshisoftData.Models.POS;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace MurshisoftData.Models;
[AddINotifyPropertyChangedInterface]
public class RestaurentHall
{
    public int HallID { get; set; }
    public string HallName { get; set; }
    public List<RestaurentTable> HallTables { get; set; }
}
[AddINotifyPropertyChangedInterface]
public class RestaurentTable
{
    public int TableID { get; set; }
    public string TableName { get; set; }
    public int TableType { get; set; }
    public int Chairs { get; set; }
    public int HallID { get; set; }
}
[AddINotifyPropertyChangedInterface]
public class RestaurantItem
{
    public string ItemID { get; set; }
    public string ItemName { get; set; }
    public decimal SalesPrice { get; set; }
    public string RefNo { get; set; }
    public string AccountNo { get; set; }
    public decimal TaxPercent { get; set; }
    //[JsonIgnore]
    //public Image Image { get; set; }
    public RestaurantCategory? Category { get; set; }
    public List<RestaurantSubItem> SubItems { get; set; } = new List<RestaurantSubItem>();


    string imgFolder => AppDomain.CurrentDomain.BaseDirectory + "\\ItemImages\\";

    public string ImageFilePath
    {
        get
        {
            var images = SessionInfoPOS.ItemImages;
            if (images == null) return defaultImage;
            var file = images.FirstOrDefault(a => a.ItemId == ItemID);
            var fileName = file?.Path ?? "";
            var path = "";
            if (fileName != "")
                path = Path.Combine(imgFolder, fileName);
            if (path == "" || !File.Exists(path)) return defaultImage;
            return path;
        }
    }
    string defaultImage => Path.Combine(imgFolder, "default.jpg");

}


[AddINotifyPropertyChangedInterface]
public class RestaurantCategory
{
    public string ItemID { get; set; } = "";
    public string ItemName { get; set; } = "";
    string imgFolder => AppDomain.CurrentDomain.BaseDirectory + "\\ItemImages\\";

    public string ImageFilePath
    {
        get
        {
            if (MySettingsPOS.PosType == PosType.NormalPOS)
                return "";
            var imgFileName = string.Format("{0}.jpg", ItemID);
            var _imgPath = Path.Combine(imgFolder, imgFileName);
            if (!File.Exists(_imgPath))
                _imgPath = Path.Combine(imgFolder, "default.jpg");
            return _imgPath;
        }
    }
    public List<RestaurantAddonItem> AddonItems { get; set; } = new List<RestaurantAddonItem>();
}
[AddINotifyPropertyChangedInterface]
public class RestaurantAddonItem
{
    public int id { get; set; }
    public string name { get; set; }
    public decimal Price { get; set; }
    public string ItemID { get; set; }
}
[AddINotifyPropertyChangedInterface]
public class RestaurantSubItem
{
    public int id { get; set; }
    public string name { get; set; }
    public string ItemID { get; set; }
    public decimal Price { get; set; }
    public decimal PriceBeforeTax { get; set; }
    [JsonIgnore]
    public RestaurantItem Stock { get; set; }
}