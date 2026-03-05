using MurshisoftData.Models.POS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MurshisoftData.Models;
public class PosSalesClosingMaster
{
    public int id { get; set; }
    public int FinancialYear { get; set; } = SessionInfoPOS.SessionData.FinancialYear;
    public int BranchId { get; set; } = SessionInfoPOS.SessionData.BranchID;
    public DateTime ClosingDate { get; set; } = DateTime.Today;
    public DateTime ClosingTime { get; set; } = DateTime.Now;
    public string ClosingUserName { get; set; }
    public int ClosingTypeId { get; set; }
    public string UserName { get; set; } = SessionInfoPOS.SessionData.UserName;
    public decimal SalesTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal NetSales { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal AmountWithTax { get; set; }
    public decimal Cash { get; set; }
    public decimal Span { get; set; }
    public decimal Credit { get; set; }
    public decimal UserPaid { get; set; }
    public decimal Balance
    {
        get { return UserPaid - Cash; }
        set { }
    }
    public string Notes { get; set; } = "";
    public List<PosSalesClosingDetail> PosSalesClosingDetails { get; set; } = new List<PosSalesClosingDetail>();
    public List<PosSalesClosingDetail> Sales { get; set; } = new List<PosSalesClosingDetail>();
    public List<PosSalesClosingDetail> Returns { get; set; } = new List<PosSalesClosingDetail>();

    //Sales Total:
    public decimal SS_SalesTotal => (from x in Sales select x.SalesTotal).Sum();
    public decimal SS_Discount => (from x in Sales select x.Discount).Sum();
    public decimal SS_NetSales => (from x in Sales select x.NetSales).Sum();
    public decimal SS_TaxAmount => (from x in Sales select x.TaxAmount).Sum();
    public decimal SS_AmountWithTax => (from x in Sales select x.AmountWithTax).Sum();
    public decimal SS_Cash => (from x in Sales select x.Cash).Sum();
    public decimal SS_Span => (from x in Sales select x.Span).Sum();
    public decimal SS_Credit => (from x in Sales select x.Credit).Sum();

    //Returns Total:
    public decimal SR_SalesTotal => (from x in Returns select x.SalesTotal).Sum();
    public decimal SR_Discount => (from x in Returns select x.Discount).Sum();
    public decimal SR_NetSales => (from x in Returns select x.NetSales).Sum();
    public decimal SR_TaxAmount => (from x in Returns select x.TaxAmount).Sum();
    public decimal SR_AmountWithTax => (from x in Returns select x.AmountWithTax).Sum();
    public decimal SR_Cash => (from x in Returns select x.Cash).Sum();
    public decimal SR_Span => (from x in Returns select x.Span).Sum();
    public decimal SR_Credit => (from x in Returns select x.Credit).Sum();

}
public class PosSalesClosingDetail
{
    public int id { get; set; }
    public int ClosingId { get; set; }
    public string TransactionID { get; set; }
    public int VoucherTypeId { get; set; }
    public string VoucherTypeName { get; set; }
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