using MurshisoftData.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MurshisoftData.Models.POS
{
    public static class SessionInfoPOS
    {
        public static JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        public static SessionData SessionData;
        public static bool IsHijri { get; set; }
        public static System.Drawing.Color AlternateColor = System.Drawing.Color.FromArgb(182, 215, 228);
        public static void UpdateSettings(List<ItemImage> images)
        {
            _images = images;
        }
        public static List<ItemImage> _images;
        public static List<ItemImage> ItemImages
        {
            get
            {
                if(_images == null)
                {
                    var path = Path.Combine(Environment.CurrentDirectory, "images.json");
                    _images = Utilities.ReadFromJsonFile<List<ItemImage>>(path);
                }
                return _images;
            }
        }

        static AppSettingsPOS _settings;
        public static AppSettingsPOS AppSettings
        {
            get
            {
                if (_settings == null)
                {
                    var path = Path.Combine(Environment.CurrentDirectory, "app.json");
                    if(File.Exists(path))
                    _settings = Utilities.ReadFromJsonFile<AppSettingsPOS>(path);
                    else _settings = new AppSettingsPOS();
                }
                return _settings;
            }
        }

        public static bool NormalWithMenu = false;
       // public static bool SalesPriceTaxInclusive = false;
       // static internal string HijriToday;
       // static public int GroupID;
       // static public DateTime FDate1;
       // static public DateTime FDate2;
       // static public string CurrentBranchName;
       // static public string CurrentLanguage;
       // static public string CurrentUserName;
       // static public string CurrentUserPassword;
       // static public bool IsAdminUser;
       // static public bool IsHijri;
       // //static public int UserBranchID;
       // static public int CurrentFinancialYear;
       // static public bool AutoPosting;
       // static public bool AllowPostedEdit;
       //public static int CurrentBranchID;
       //public static int SpecialID;

       //public static DateTime LastStockTakeDate;
       //public static System.Data.DataTable StockItems;
       //public static string CashAccountNo = "1-2-1";
       //public static string BankAccountNo = "";
        public static bool IsMagsala = false;
       public static bool IsRestaurant;
    }

    public class NotifyTask
    {
        public int HallId { get; set; }
        public int SerialNo { get; set; }
        public string TransactionId { get; set; }
        public string Printer { get; set; }
        public bool Done { get; set; }
    }
    // Change the accessibility of ItemImage from internal to public to match the public method parameter usage.
    public class ItemImage
    {
        public string ItemId { get; set; }
        public string Path { get; set; }
    }
}
