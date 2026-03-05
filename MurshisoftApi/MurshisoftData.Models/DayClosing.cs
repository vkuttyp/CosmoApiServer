using MurshisoftData.Models.POS;
using System;
using System.Collections.Generic;

namespace MurshisoftData.Models
{
    public class DayClosing
    {
        public int ClosingID { get; set; }
        public DateTime ClosingDate { get; set; } = DateTime.Now;
        public int BranchID { get; set; } = SessionInfoPOS.SessionData.BranchID;
        public int FinancialYear { get; set; } = SessionInfoPOS.SessionData.FinancialYear;
        public string UserName { get; set; } = SessionInfoPOS.SessionData.UserName;
        public decimal CostPriceTotal { get; set; }
        public decimal SalesPriceTotal { get; set; }
        public decimal RoundAmountTotal { get; set; }
        public decimal Discount { get; set; }
        public decimal NetSales
        {
            get
            {
                return SalesPriceTotal - (Discount + RoundAmountTotal);
            }
        }

        public decimal CashAmount { get; set; }
        public decimal SpanAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public string VoucherNo { get; set; } = "";
        public DayClosingDetails DayClosingDetails { get; set; }

        //public void DaySalesLoad(int branchId)
        //{
        //    BranchID = branchId;
        //    UserName = SessionInfo.SessionData.UserName;
        //    FinancialYear = SessionInfo.SessionData.FinancialYear;
        //    VoucherNo = "";
        //    DayClosingDetails = DataAccess.GetDayClosingDetailByDay(branchId);
        //    var t = from detail in DayClosingDetails
        //            select detail.CostPrice;
        //    CostPriceTotal = t.Sum();
        //    SalesPriceTotal = DayClosingDetails.Sum(s => s.SalesPrice);
        //    Discount = DayClosingDetails.Sum(d => d.InvoiceDiscount + d.LineItemDiscountTotal);
        //    RoundAmountTotal = DayClosingDetails.Sum(r => r.RoundAmount);
        //    CashAmount = DayClosingDetails.Sum(r => r.Cash);
        //    SpanAmount = DayClosingDetails.Sum(r => r.Span);
        //    CreditAmount = DayClosingDetails.Sum(r => r.Credit);

        //}
    }

    public class DayClosingDetail
    {
        public int id { get; set; }
        public int ClosingID { get; set; }
        public string TransactionID { get; set; }
        public decimal CostPrice { get; set; }
        public decimal SalesPrice { get; set; }
        public decimal RoundAmount { get; set; }
        public decimal LineItemDiscountTotal { get; set; }
        public decimal InvoiceDiscount { get; set; }
        public decimal NetAmount
        {
            get
            {
                return SalesPrice - InvoiceDiscount;
            }
        }
        public decimal Cash { get; set; }
        public decimal Span { get; set; }
        public decimal Credit { get; set; }

    }

    public class DayClosings : List<DayClosing> { }
    public class DayClosingDetails : List<DayClosingDetail> { }
}
