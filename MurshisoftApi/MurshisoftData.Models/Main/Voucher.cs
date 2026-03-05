using MurshisoftData.Models;
using MurshisoftData.Models.Main;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace MurshisoftData.Main;

[AddINotifyPropertyChangedInterface]
public class VoucherMain
{
    public string VoucherNo { get; set; } = "";
    public DateTime VoucherDate { get; set; } = DateTime.Today;
    public DateTime VoucherTime { get; set; }= DateTime.Now;
    public string OtherNo { get; set; } = "";
    public string BillTo { get; set; } = "";
    public string Details { get; set; } = "";
    public string UserName { get; set; } = "";
    public int VoucherTypeID { get; set; }
    public string ChequeNo { get; set; } = "";
    public DateTime ChequeDueDate { get; set; }=DateTime.Now;
    public bool ChequeType { get; set; }
    public bool PendingInvoice { get; set; }
    public int FinancialYear { get; set; }
    public int sno { get; set; }
    public int BranchID { get; set; }
    public string VoucherDate2 { get; set; } = "";
    public DateTime AccountingDate { get; set; }=DateTime.Now;
    public bool Posted { get; set; } = true;
    public int PaymentMethodID { get; set; }
    public decimal BalanceAmount { get; set; }
    public int PaymentModeID { get; set; }
    public string VoucherTypeMenuName { get; set; } = "";
    public decimal DebitAmountTotal { get; set; } //=> VoucherDetails?.FastSum(a => a.DebitAmount) ?? 0;
    public decimal CreditAmountTotal { get; set; } // => VoucherDetails?.FastSum(a => a.CreditAmount) ?? 0;
    public decimal Difference => DebitAmountTotal - CreditAmountTotal;
    public MyList<VoucherDetail> VoucherDetails { get; set; } = [];
    public VoucherType? VoucherType { get; set; }
    public Branch? Branch { get; set; }
}

[AddINotifyPropertyChangedInterface]
public class VoucherDetail
{
    public string VoucherNo { get; set; } = "";
    public string AccountNo { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Details2 { get; set; } = "";
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public int SerialNo { get; set; }
    public int CostCenterID { get; set; }
    public string DocumentNo { get; set; } = "";
    public string LineId { get; set; } = "";
    public Branch? CostCenter { get; set; }

}

[AddINotifyPropertyChangedInterface]
public class VoucherNavation
{
    public string VoucherNo { get; set; }
    public int VoucherNoInt { get; set; }
    public int SerialNo { get; set; }
    public int RecordsCount { get; set; }
    public long RowNumber { get; set; }
    public string RowDisplay => $"{RowNumber} / {RecordsCount}";
}

[AddINotifyPropertyChangedInterface]
public class VoucherInvoiceMain
{
    public int id { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public int FinancialYear { get; set; } 
    public int BranchId { get; set; } 
    public string UserName { get; set; } 
    public int SupplierId { get; set; }
    public string VoucherNo { get; set; } = "";
    public int SerialNo { get; set; }
    public int VoucherTypeId { get; set; }
    public string ExpenseAcNo { get; set; }
    public decimal ExpenseAmount { get; set; }
    public string TaxAcNo { get; set; }
    public decimal TaxAmount { get; set; }
    public string CreditAccountNo { get; set; }
    public decimal CreditAmount { get; set; }
    public string Notes { get; set; } = "";
    public bool Changed { get; set; }
    public VoucherAccount ExpenseAccount { get; set; } = new();
    public VoucherAccount TaxAccount { get; set; } = new();
    public VoucherAccount CreditAccount { get; set; } = new();
    public List<VoucherInvoiceDetail> Invoices { get; set; } = new();
    public decimal Amount => Invoices?.Sum(a => a.Amount) ?? 0;
    public decimal TaxTotal => Invoices?.Sum(a => a.TaxAmount) ?? 0;
    public decimal TotalAmount => Invoices?.Sum(a => a.TotalAmount) ?? 0;
}
[AddINotifyPropertyChangedInterface]
public class VoucherInvoiceDetail
{
    public int id { get; set; }
    public int ParentId { get; set; }
    public int SupplierId { get; set; }
    public VoucherSupplier VoucherSupplier { get; set; } = new();
    public string Details { get; set; } = "";
    public string InvoiceNo { get; set; } = "";
    public DateTime InvoiceDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

}
[AddINotifyPropertyChangedInterface]
public class VoucherSupplier :IEquatable<VoucherSupplier>
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string VatNo { get; set; } = "";


    public bool Equals(VoucherSupplier other)
    {
        if (other == null) return false;
        return this.id == other.id;
    }
}
[AddINotifyPropertyChangedInterface]
public class VoucherAccount
{
    public string AccountNo { get; set; } = "";
    public string AccountName { get; set; } = "";
    public int CostCenterId { get; set; }
    public decimal Amount { get; set; }

}