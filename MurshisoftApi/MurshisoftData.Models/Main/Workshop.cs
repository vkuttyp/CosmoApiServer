using MurshisoftData.Models;
using PropertyChanged;
using System;
using System.Collections.Generic;

namespace MurshisoftData.Main;
public class CostingContract
{
    public string ContractID { get; set; }
    public string CustomerName { get; set; }
    public List<ContractCostingDetail> Details { get; set; } = [];

}
[AddINotifyPropertyChangedInterface]
public class ContractCostingMain
{
    public int id { get; set; }
    public DateTime CostingDate { get; set; } = DateTime.Today;
    public int BranchID { get; set; }
    public string BranchName { get; set; }
    public int FinancialYear { get; set; } 
    public string ContractID { get; set; }
    public string TransactionID { get; set; }
    public string UserName { get; set; } 
    public bool CanEdit => id > 0;
    public CompanyInfo? CompanyInfo { get; set; }
    public string PrintedBy { get; set; }
    public List<CostingContract> Contracts { get; set; } = [];

}
[AddINotifyPropertyChangedInterface]
public class ContractCostingDetail
{
    public int id { get; set; }
    public int EntryId { get; set; }
    public string ContractId { get; set; } = "";
    public string CustomerName { get; set; }
    public string VoucherNo { get; set; } = "";
    public string ItemID { get; set; }
    public string ItemName { get; set; }
    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }

}
public class InvoiceItem
{
    public string ItemId { get; set; }
    public string ItemName { get; set; }
    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }
}