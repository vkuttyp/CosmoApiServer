using MurshisoftData.Models.POS;
using PropertyChanged;
using System;
using System.Collections.Generic;

namespace MurshisoftData.Models;

[AddINotifyPropertyChangedInterface]
public class ItemCardCategory: ItemCard
{
    public MySortableBindingList<ItemCard> Items { get; set; } = [];
}
[AddINotifyPropertyChangedInterface]
public class ItemCard
{
     public string ItemID { get; set; } = "";
    public string RefNo { get; set; } = "";
    public string PartNo { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string ItemNameEnglish { get; set; } = "";
    public bool IsProduct { get; set; }
    public string Location { get; set; } = "";
    public decimal ProfitPercent { get; set; }
    public decimal LeastProfitPercent { get; set; }
    public int NumberOfUnits { get; set; }
    public decimal DiscountAmount { get; set; } = 0;
    public bool IsMahata { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalesPrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal OrderLevel { get; set; }
    public bool Discontinued { get; set; }
    public string UserName { get; set; } = SessionInfoPOS.SessionData?.UserName ?? "";
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public int ItemSerial { get; set; }
    public bool IsMain { get; set; }
    public bool AutoNum { get; set; }
    public decimal WholesalePrice { get; set; }
    public bool IsService { get; set; }
    public int ManufacturerID { get; set; }
    public int StockistID { get; set; }
    public decimal OrderLevelMaximum { get; set; }
    public string Barcode { get; set; } = "";
    public bool HasDetails { get; set; }
    public int OrderSerial { get; set; }
    public int AssembledProduct { get; set; }
    public string AccountNo { get; set; } = "";
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal BiggestUnitCount { get; set; }
    [DoNotNotify]
    public List<Unit>? Units { get; set; } = [];
    public List<AltPartNo>? AltPartNos { get; set; } = [];
    public List<SubItem>? SubItems { get; set; } = [];
}
[AddINotifyPropertyChangedInterface]
public class Unit
{
    public int UnitID { get; set; }
    public string ItemID { get; set; } = "";
    public string UnitName { get; set; }= "";
    public decimal NumOfPieces { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalesPrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal WholeSalePrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Barcode { get; set; } = "";
}
public class AltPartNo
{
    public string ItemID { get; set; }
    public string PartNo { get; set; }
    public bool AutoTransfer { get; set; }
}