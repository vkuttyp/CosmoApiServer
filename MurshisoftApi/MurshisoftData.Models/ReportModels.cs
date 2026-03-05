using System;
using System.Collections.Generic;

namespace MurshisoftData.Models;

public class ClosingMaster
{
    public int id { get; set; }
    public int FinancialYear { get; set; }
    public int BranchId { get; set; }
    public DateTime ClosingDate { get; set; }
    public DateTime ClosingTime { get; set; }
    public string ClosingUserName { get; set; }
    public int ClosingTypeId { get; set; }
    public string UserName { get; set; }
    public decimal SalesTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal NetSales { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal AmountWithTax { get; set; }
    public decimal Cash { get; set; }
    public decimal Span { get; set; }
    public decimal Credit { get; set; }
    public decimal UserPaid { get; set; }
    public decimal Balance { get; set; }
    public string Notes { get; set; } = "";
    public List<ClosingDetail> ClosingDetails { get; set; }
}
public class ClosingDetail
{
    public int id { get; set; }
    public int ClosingId { get; set; }
    public string TransactionID { get; set; }
    public int DailySerial { get; set; }
    public int VoucherTypeId { get; set; }
    public int SerialNo { get; set; }
    public decimal SalesTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal NetSales { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal AmountWithTax { get; set; }
    public decimal Cash { get; set; }
    public decimal Span { get; set; }
    public decimal Credit { get; set; }


}