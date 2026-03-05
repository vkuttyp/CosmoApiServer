using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MurshisoftData.Models.Main;

[AddINotifyPropertyChangedInterface]
public class StatementMaster
{
    public int FinancialYear { get; set; }
    public string AccountNo { get; set; }
    //public string AccountName { get; set; }
    public string VatNumber { get; set; }
    public int CreditPeriod { get; set; }
    public int AccountTypeId { get; set; }
    public string AcountName { get; set; } = "";// => StatementDetails?.FirstOrDefault()?.AccountName ?? "";
    public decimal OpeningBalance { get; set; }
    public string UserName {get; set; }
    public int VoucherTypeID { get; set; }
    public string VoucherTypeName { get; set; } = "";
    public int CostCenterID { get; set; }
    public string CostCenterName { get; set; } = "";
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string UserInterface { get; set; } = "";
    public MySortableBindingList<StatementDetail> StatementDetails { get; set; } = [];
    public decimal DebitAmountTotal { get; set; }// => StatementDetails?.FastSum(s => s.DebitAmount) ?? 0;

    public decimal CreditAmountTotal { get; set; }// => StatementDetails?.FastSum(s => s.CreditAmount) ?? 0;

    public decimal NetBalance { get; set; }// => (OpeningBalance + DebitAmountTotal) - CreditAmountTotal;

    public string ToWords { get; set; } = "";//=> Accounts.VoucherPrint.GetToWord(Math.Abs(NetBalance));

    public CompanyInfo? CompanyInfo { get; set; }
    //public System.Drawing.Image Logo => System.Drawing.Image.FromFile("logo.jpg");

    public static Dictionary<int, string> SearchFields { get; set; }// => DataAccess.GetSearchFields();
    public int TotalRows { get; set; }
    public int SearchBy { get; set; }
    public string? SearchKeyword { get; set; }
    //public string CreditAgeText
    //{
    //    get
    //    {
    //        return String.Join(" | ",
    //                 CreditAges?.Select(a => String.Join(", ", $"{a.name}: SAR.{a.amount}"))) ?? "";
    //    }
    //}
    public List<CreditAge>? CreditAges
    {
        get
        {
            if (CreditPeriod == 0) CreditPeriod = 30;
            var days = UserInterface == "Arabic" ? "يوم" : "Days";
            var morethan = UserInterface == "Arabic" ? "أكثر من:" : "More than";
            return StatementDetails?.Where(a => a.InvoiceAge > 0)
                .GroupBy(item =>
                {
                    if (item.InvoiceAge < CreditPeriod) return 1;
                    else if (item.InvoiceAge < CreditPeriod * 2) return 2;
                    else if (item.InvoiceAge < CreditPeriod * 3) return 3;
                    else if (item.InvoiceAge < CreditPeriod * 4) return 4;
                    else return 5;
                })
                .Select(g => new CreditAge
                {
                    id = g.Key,
                    name = g.Key switch
                    {
                        1 => $"1-{CreditPeriod} {days}",
                        2 => $"{CreditPeriod}-{CreditPeriod * 2} {days}",
                        3 => $"{CreditPeriod * 2}-{CreditPeriod * 3} {days}",
                        4 => $"{CreditPeriod * 3}-{CreditPeriod * 4} {days}",
                        5 => $"{morethan} {CreditPeriod * 4} {days}",
                        _ => "Unknown"
                    },
                    amount = g.Sum(item => item.DebitAmount - item.AmountPaid)
                })
                .OrderBy(ca => ca.id)
                .ToList();
        }
    }
    public CreditAgeTable CreditAgeTable
    {
        get
        {
            if (CreditPeriod == 0) CreditPeriod = 30;
            var days = UserInterface == "Arabic" ? "يوم" : "Days";
            var morethan = UserInterface == "Arabic" ? "أكثر من:" : "More than";
            var creditAges = CreditAges;
            return new CreditAgeTable
            {
                Item1 = creditAges?.Count > 0 ? new TableItem
                {
                    Heading = creditAges[0].name,
                    Amount = creditAges[0].amount.ToString("N2")
                } : new TableItem { Heading = $"1-{CreditPeriod} {days}", Amount = "0.00" },
                Item2 = creditAges?.Count > 1 ? new TableItem
                {
                    Heading = creditAges[1].name,
                    Amount = creditAges[1].amount.ToString("N2")
                } : new TableItem { Heading = $"{CreditPeriod}-{CreditPeriod * 2} {days}", Amount = "-" },
                Item3 = creditAges?.Count > 2 ? new TableItem
                {
                    Heading = creditAges[2].name,
                    Amount = creditAges[2].amount.ToString("N2")
                } : new TableItem { Heading = $"{CreditPeriod * 2}-{CreditPeriod * 3} {days}", Amount = "-" },
                Item4 = creditAges?.Count > 3 ? new TableItem
                {
                    Heading = creditAges[3].name,
                    Amount = creditAges[3].amount.ToString("N2")
                } : new TableItem { Heading = $"{CreditPeriod * 3}-{CreditPeriod * 4} {days}", Amount = "-" },
                Item5 = creditAges?.Count > 4 ? new TableItem
                {
                    Heading = creditAges[4].name,
                    Amount = creditAges[4].amount.ToString("N2")
                } : new TableItem { Heading = $"{morethan} {CreditPeriod * 4} {days}", Amount = "-" }
            };
        }
    }
}
public class TableItem
{
    public string Heading { get; set; } = "";
    public string Amount { get; set; } = "";
}
public class CreditAgeTable
{
    public TableItem? Item1 { get; set; }
    public TableItem? Item2 { get; set; }
    public TableItem? Item3 { get; set; }
    public TableItem? Item4 { get; set; }
    public TableItem? Item5 { get; set; }
}
public class CreditAge
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public decimal amount { get; set; }
}
[AddINotifyPropertyChangedInterface]
public class StatementDetail
{
    public string AccountNo { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string VoucherTypeName { get; set; } = "";
    public string VoucherNo { get; set; } = "";
    public DateTime VoucherDate { get; set; }
    public string VoucherDate2 { get; set; } = "";
    public int VoucherTypeID { get; set; }
    public int BranchID { get; set; }
    public string BranchName { get; set; } = "";
    public string OtherNo { get; set; } = "";
    public string ChequeNo { get; set; } = "";
    public string Details2 { get; set; } = "";
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal NetBalance { get; set; }
    public decimal AmountPaid { get; set; }
    public bool HasDocs { get; set; }
    public int PaymentStatus { get; set; }
    public int InvoiceAge { get; set; }
}