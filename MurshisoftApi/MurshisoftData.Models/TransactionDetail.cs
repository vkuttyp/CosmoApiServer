using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MurshisoftData.Models;


[AddINotifyPropertyChangedInterface]
public class TransactionDetail
{
    public int VoucherTypeID;

    public int TransactionDetailID { get; set; }
    public string TransactionID { get; set; } = "";
    public string ItemID { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Carat { get; set; } = "";
    private decimal _PiecesCount;
    public decimal PiecesCount { get; set; }
    public decimal SalesPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal Bonus { get; set; }
    public decimal SalesPriceAfterDiscount => DiscountPercent>0 ? MyHelpers.Round(SalesPrice / (1 + DiscountPercent), 2) : SalesPrice;
    public decimal SalesPriceTotal { get; set; } //=>  MyHelpers.Round(SalesPrice * Quantity, 2);
    public decimal CostPriceTotal { get; set; } //=> MyHelpers.Round(CostPrice * Quantity, 2);
    public decimal Discount { get; set; }
    public decimal DiscountPercent { get; set; }
    public int UnitID { get; set; }
    public string UnitName { get; set; } = "";
    public decimal NumOfPieces { get; set; } = 1;
    public string Description { get; set; } = "";
    public DateTime? ExpiryDate { get; set; }
    public string RackID { get; set; } = "";
    public decimal LastPrice { get; set; }
    public string ContractID { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string ItemDescription { get; set; } = "";
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal NetSalesPrice { get; set; } //=> SalesPriceTotal - Discount;
    public decimal NetCostPrice { get; set; } //
    //{
    //    get
    //    {
    //        if (PriceTypes.GetPriceType(VoucherTypeID)== Models.PriceType.CostPrice) //PriceType.CostPrice)
    //            return CostPrice * Quantity - Discount;
    //        else return CostPrice * Quantity;
    //    }
    //}
    public decimal RoundValue { get; set; }
    public decimal ActualPrice { get; set; } //=> AmountWithTax;// { get; set; }
    public decimal SupposedQty { get; set; }
    public int SubItemID { get; set; }
    public RestaurantSubItem? SubItem { get; set; }
    public List<ItemDetail> ItemDetails { get; set; } = new ();
    public List<ItemAddon>? ItemAddons { get; set; } = new List<ItemAddon>();
    public RestaurantItem? Stock { get; set; }
    public RestaurantCategory? Category { get; set; }
    //public int UpdateMode { get; set; }
    public bool HasDetails => Length > 0;
    public decimal Balance { get; set; }
    public decimal TaxPercent { get; set; }
    //public decimal TaxAmount => TaxPercent == 0 ? 0 : MyHelpers.Round(NetSalesPrice * TaxPercent, 2);
    public decimal AmountWithTax =>  NetSalesPrice + TaxAmount;
    public bool TaxInclusive { get; set; }
    //public bool HasDetails => Length > 0;
    public StockItemView? StockItemView { get; set; }

    public string? SearchButton => "F1";
    //public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }

    public decimal ItemPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public PriceType? PriceType => new TransactionType(VoucherTypeID).PriceType;
    public decimal ProfitPercent
    {
        get
        {
            if (CostPrice == 0) return 1;
            var profit = SalesPrice - CostPrice;
            if (profit == 0 || SalesPrice == 0) return 0;

            return (MyHelpers.Round(profit / SalesPrice, 2));
        }
    }
    public FooterInfo? FooterInfo { get; set; } = new FooterInfo();
}

[AddINotifyPropertyChangedInterface]
public class ItemAddon
{
    public int id { get; set; }
    public string TransactionID { get; set; }
    public string ItemID { get; set; }
    public int SubItemId { get; set; }
    public int AddonId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal Price { get; set; }
}
[AddINotifyPropertyChangedInterface]
public class ItemDetail
{
    public int CalculationMode { get; set; }
    public bool IncludeHeight { get; set; }
    public int id { get; set; }
    public string? TransactionID { get; set; } = "";
    public int TransactionDetailID { get; set; }
    public string? ItemID { get; set; } = "";
    public string? Description { get; set; } = "";
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal Quantity { get; set; }
    public decimal TotalSize { get; set; }

}
[AddINotifyPropertyChangedInterface]
public class StockItemView
{
    public string? ItemID { get; set; } = "";
    public bool IsBarcode { get; set; }
    public string? Barcode { get; set; } = "";
    public string? ItemName { get; set; } = "";
    public string? ItemNameEnglish { get; set; } = "";
    public string? Location { get; set; } = "";
    public decimal CostPrice { get; set; }
    public decimal SalesPrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal WholeSalePrice { get; set; }
    public bool HasDetails { get; set; }
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal LastPurchasePrice { get; set; }
    public string? ItemNotes { get; set; } = "";
    public decimal QuantityOnHand { get; set; }
    public decimal PiecesOnHand { get; set; }
    public string? QuantityUnit { get; set; } = "";
    public int NumberOfUnits { get; set; }
    public decimal TransactionBalance { get; set; }
    public string? RackID { get; set; } = "";
    public DateTime ExpiryDate { get; set; }
    public decimal BiggestUnitCount { get; set; } // used for Package size
    public decimal DiscountAmount { get; set; } // used for Taxpercent
    public decimal LastSalesPrice { get; set; }
    public string? QuantityDisplay
    {
        get
        {
            if (HasDetails)
                return PiecesOnHand.ToString("f");
            else return QuantityUnit;
        }
    }
    //[JsonIgnore]
    //public System.Drawing.Color OnHandBackColor
    //{
    //    get
    //    {
    //        return QuantityOnHand < 1 ? System.Drawing.Color.Red : System.Drawing.Color.Empty;
    //    }
    //}
    public string? Carat { get; set; } = "";
    public string? UnitName { get; set; } = "";
    public List<Unit>? Units { get; set; } = [];
    public List<SubItem>? SubItems { get; set; } = [];
    public List<AlternativePart>? AlternativeParts { get; set; } = [];
    public List<ProductExpiry>? ProductExpiries { get; set; } = [];
    public StockItemView()
    {
        AlternativeParts = [];
        QuantityOnHand = 0;
        ExpiryDate = DateTime.Today;
        RackID = "";
    }
}
[AddINotifyPropertyChangedInterface]
public class SubItem
{
    public int id { get; set; }
    public string? name { get; set; } = "";
    public string? ItemID { get; set; } = "";
    public int TypeId { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalesPrice { get; set; }
    public decimal PriceBeforeTax { get; set; }
    public string Barcode { get; set; }
    public bool Changed { get; set; }
    [JsonIgnore]
    public RestaurantItem? Stock { get; set; }

}
[AddINotifyPropertyChangedInterface]
public class ProductRawMaterial
{
    public int id { get; set; }
    public string? ItemID { get; set; } = "";
    public int SubItemId { get; set; }
    public string? RawMaterialID { get; set; } = "";
    public RawMaterialDetail? RawMaterial { get; set; }
    public string? RawMaterialName => RawMaterial?.ItemName ?? string.Empty;
    public decimal Quantity { get; set; }
    public bool Changed { get; set; }
    public decimal RawMaterialCost => RawMaterial?.CostPrice ?? 0;
    public decimal CostTotal => Quantity * RawMaterialCost;
}
[AddINotifyPropertyChangedInterface]
public class RawMaterialDetail
{
    public int RawMaterialId { get; set; }
    public string? ItemId { get; set; } = "";
    public int UnitId { get; set; }
    public decimal NumOfPieces { get; set; } = 1;
    public decimal CostPrice { get; set; }
    public string? ItemName { get; set; } = "";
    public bool Changed { get; set; }
}
[AddINotifyPropertyChangedInterface]
public class AlternativePart
{
    public string? PartNo { get; set; } = "";
    public decimal Balance { get; set; }
}
[AddINotifyPropertyChangedInterface]
public class ProductExpiry
{
    public DateTime? ExpiryDate { get; set; }
    public decimal Balance { get; set; }
}
//public enum PriceType { None = 0, SalesPrice = 1, CostPrice = 2 }

[AddINotifyPropertyChangedInterface]
public class FooterInfo
{
    public string? ItemId { get; set; } = "";
    public decimal Balance { get; set; }
    public decimal PiecesOnHand { get; set; }
    public decimal LastPurchasePrice { get; set; }
    public decimal LastSalesPrice { get; set; }
    public decimal LastPrice { get; set; }
    public string? Location { get; set; } = "";
    public decimal SalesPrice { get; set; }
    public decimal CostPrice { get; set; }
    public string? QuantityDisplay => Balance.ToString("f2");
    public string? BalanceString => Common.CalculateUnitFooter(Balance, ItemId, Units);
    public List<Unit> Units { get; set; } = [];

}
