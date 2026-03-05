using MurshisoftData.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MurshisoftData.Main;
public class Invoice
{
    //static CustomSettings.MySettings set = new CustomSettings.MySettings().Load();
    public string TransactionID { get; set; }
    public DateTime TransactionDate { get; set; }
    public string TransactionDate2 => MyHelpers.ConvertToHijri2(TransactionDate);
    public DateTime TransactionTime { get; set; }
    public DateTime PaymentDueDate { get; set; }
    public string DocumentNo { get; set; }
    public int BranchID { get; set; }
    public string BranchName { get; set; }
    public int VoucherTypeID { get; set; }
    public string VoucherTypeName { get; set; }
    public string CustomerID { get; set; }
    public string CustomerName { get; set; }
    public string UserName { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal LineItemDiscount => LineItems?.Sum(a => a.Discount) ?? 0;
    public decimal Discount { get; set; }// => LineItemDiscount;
    public decimal NetAmount { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CashAmount2 { get; set; }
    public decimal CreditAmount { get; set; }
    public string PaymentMethod { get; set; }
    public decimal CustomerBalance { get; set; }
    public string Notes { get; set; }
    public string PrintedBy { get; set; }
    public string NumberToWord => new ToWord(NetAmount).ConvertToArabic();
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAfterDiscount => TotalAmount - Discount;
    public CompanyInfo CompanyInfo { get; set; }
    public Customer Customer { get; set; }
    public List<LineItem> LineItems { get; set; }
    public decimal CostPriceTotal => LineItems?.Sum(a => a.CostPriceTotal) ?? 0;
    public decimal SalesPriceTotal => LineItems?.Sum(a => a.SalesPriceTotal) ?? 0;
    public decimal QuantityTotal => LineItems?.Sum(a => a.Quantity) ?? 0;
    public decimal DiscountTotal => LineItems?.Sum(a => a.Discount) ?? 0;
    public string CustomerVat { get; set; }
    public decimal ItemsCount => LineItems?.Sum(a => a.Quantity) ?? 0;
    public string SpanType { get; set; }

    public List<PaymentDetail> PaymentDetails { get; set; }

    public PaymentIfo PaymentInfo
    {
        get
        {
            var inf = new PaymentIfo();
            inf.CashAccount = PaymentDetails?.FirstOrDefault(a => a.PaymentTypeId == 2 && a.Amount != 0)?.AccountName ?? "";
            inf.BankAccount = PaymentDetails?.FirstOrDefault(a => a.PaymentTypeId == 3 && a.Amount != 0)?.AccountName ?? "";
            return inf;
        }
    }
}
public class PaymentDetail
{
    public int PaymentTypeId { get; set; }
    public string AccountNo { get; set; }
    public string AccountName { get; set; }
    public decimal Amount { get; set; }
}
public class PaymentIfo
{
    public string CashAccount { get; set; }
    public string BankAccount { get; set; }

}
public class Customer
{
    public string AccountNo { get; set; }
    public string AccountName { get; set; }
    public string IDNo { get; set; }
    public string Address { get; set; }
    public string FullAddress => PostalAddress?.FullAddress == null ? Address : PostalAddress.FullAddress.Replace('\n', '-');
    public string TelNo { get; set; }
    public string MobileNo { get; set; }
    public decimal CreditLimit { get; set; }
    public int CreditPeriod { get; set; }
    public string ContactPerson { get; set; }
    public PostalAddress PostalAddress { get; set; }
}
public class LineItem
{
    public int TransactionDetailID { get; set; }
    public string ItemID { get; set; }
    public string ItemName { get; set; }
    public string Barcode { get; set; }
    public decimal Quantity { get; set; }
    public decimal NumOfPieces { get; set; }
    public decimal CostPrice { get; set; }
    public decimal CostPriceTotal => CostPrice * Quantity;
    public decimal SalesPrice { get; set; }
    public decimal SalesPriceTotal => SalesPrice * (Quantity + Bonus);
    public decimal Discount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal NetCostPrice => CostPriceTotal - Discount;
    public decimal NetSalesPrice => SalesPriceTotal - Discount;
    public decimal Bonus { get; set; }
    public string ContractID { get; set; }
    public string Description { get; set; }
    public string ItemDescription { get; set; }
    public DateTime ExpiryDate { get; set; }
    public decimal Length { get; set; }
    public decimal Height { get; set; }
    public decimal PiecesCount { get; set; }
    public string RackID { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal AmountWithTax => NetSalesPrice + TaxAmount;
    public List<ItemDetail> ItemDetails { get; set; }
    public Unit Unit { get; set; }
    public string AccountNo { get; set; } = "PCE";
    public string UnitName => Unit?.UnitName ?? AccountNo;
    public int ItemDetailsCount => ItemDetails.Count;
}
public class PostalAddress
{
    public int id { get; set; }
    public string CustomerId { get; set; }
    public string BuildingNumber { get; set; } = "";
    public string PlotIdentification { get; set; } = "";//Additional Number
    public string StreetName { get; set; } = "";
    public string AdditionalStreetName { get; set; } = "";//Unit No
    public string PostalZone { get; set; } = "";
    public string CityName { get; set; } = "";
    public string CountrySubentity { get; set; } = "";
    public string CitySubdivisionName { get; set; } = "";
    public string Country { get; set; } = "SA";
    public string ShortAddress { get; set; } = "";
    public string FullAddress
    {
        get
        {
            var line1 = $"{BuildingNumber} {StreetName}\n";
            var line2 = $"{PlotIdentification} {CitySubdivisionName}";
            var line3 = $"{CityName} - {PostalZone}";
            var full = ShortAddress != "" ? ShortAddress + Environment.NewLine : "";
            return full + line1 + line2 + line3;
        }
    }
}
