using System;
using System.Linq;

namespace MurshisoftData.Models;

public class SalesDetail
{
    public decimal Cash { get; set; }
    public decimal Paid { get; set; }
    public decimal Balance => Cash - Paid;      

    public decimal Sarraf { get; set; }
    public decimal Credit { get; set; }
    public decimal Total=> Cash + Sarraf + Credit;
   
}
public class BarcodeSearchResult
{
    public string Barcode { get; set; }
    public string ItemName { get; set; }
    public string UnitName { get; set; }
    public decimal SalesPrice { get; set; }
}
public class PosItemDetail
{
    public decimal PreviousExpiryCount { get; set; }
    public decimal Balance { get; set; }
    public int UnitID { get; set; }
    public string ItemID { get; set; }
    public string Barcode { get; set; }
    public string ItemName { get; set; }
    public string UnitName { get; set; }
    public decimal NumOfPieces { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalesPrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal WholeSalePrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public string ItemNameEnglish { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal DiscountPercent { get; set; }
    public bool TaxInclusive { get; set; }
    public DateTime ExpiryDate { get; set; }
    public string RackID { get; set; }
    public string Dose { get; set; }
}
