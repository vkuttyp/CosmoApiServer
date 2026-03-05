using System;
using System.Collections.Generic;
using System.Text;

namespace MurshisoftData.Models.POS;
public class AppSettingsPOS
{
    public bool EnableCacheApi { get; set; }
    public string CacheApiUrl { get; set; }
    public string DisplayPolePort { get; set; } = "";
    public bool ShowBalanceInResSales { get; set; }
    public bool HasCustomerDisplayPole { get; set; }
    public bool HasComBarcodeScanner { get; set; }
    public string BarcodeScannerPort { get; set; } = "";
    public bool HasPaymentTerminal { get; set; }
    public string SpanPortName { get; set; } = "";
    public bool IsMag { get; set; }
    public bool PrintSecondCopy { get; set; }
    public bool QuantityMerge { get; set; } = true;
    public bool Search4Digits { get; set; } = true;
    public bool AllowPriceEditToNonAdmin { get; set; }
    public bool AllowNormalUserReturn { get; set; } = true;
    public bool ShowReport { get; set; }
    public bool ShowInvoices { get; set; }
    public int RHeight { get; set; } = 100;
    public int RWidth { get; set; } = 170;
    public int RCount { get; set; } = 4;
    public bool DefaultCash { get; set; }
    public bool EnableMizan { get; set; }
    public string MizanDigits { get; set; } = "99";
    public int MizanType { get; set; }
    public int BranchId { get; set; }
}