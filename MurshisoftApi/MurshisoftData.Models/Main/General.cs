using System;
using System.Collections.Generic;
using System.Text;

namespace MurshisoftData.Models.Main;

public class ParentAccount
{
    public string AccountNo { get; set; }
    public string AccountName { get; set; }
    public List<ChildAccount> ChildAccounts { get; set; } = new();
}
public class ChildAccount
{
    public string AccountNo { get; set; }
    public string AccountName { get; set; }
    public string MobileNo { get; set; }
    public string FaxNo { get; set; }
    public string Address { get; set; }
    public string RefNo { get; set; }
}
public class FinancialYearModel
{
    public int FinancialYear { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public bool IsCurrentYear { get; set; }
    public int Sn { get; set; }
    public string UserName { get; set; }
    public DateTime EntryDate { get; set; }
}
public class Branch
{
    public int BranchId { get; set; }
    public string BranchName { get; set; }
    public int ParentId { get; set; }
    public int Level { get; set; }
}
public class Menu
{
    public string MenuID { get; set; }
    public string MenuName { get; set; }
    public string MenuParent { get; set; }
    public string MenuEnable { get; set; }
    public string Shortcut { get; set; }
    public string IconIndex { get; set; }
    public int sno { get; set; }
    public GroupRight GroupRight { get; set; }
    public List<Menu> SubMenu { get; set; }
}
public class GroupRight
{
    public string MenuID { get; set; }
    public bool Opening { get; set; }
    public bool Adding { get; set; }
    public bool Editing { get; set; }
    public bool Deleting { get; set; }
}
public class MenuToolBar
{
    public string MenuID { get; set; }
    public string MenuName { get; set; }
    public byte[] ToolBarImage { get; set; }
    public byte[] ToolBarHotImage { get; set; }
}
public class StockViewPar
{
    public StockViewPar(string id, int branchId, int financialyear, string customerId, string transactionId, int unitId)
    {
        this.id = id;
        this.branchId = branchId;
        this.financialyear = financialyear;
        this.customerId = customerId;
        this.transactionId = transactionId;
        this.unitId = unitId;
    }

    public string id { get; set; }
    public int branchId { get; set; }
    public int financialyear { get; set; }
    public string customerId { get; set; }
    public string transactionId { get; set; }
    public int unitId { get; set; }
    public string DefaultUnitName { get; set; } = "Piece";
    public string Key => $"{id}{branchId}{financialyear}{customerId}{transactionId}{unitId}";
}
public class TransactionNavigation
{
    public string TransactionID { get; set; }
    public int TransactionIDInt { get; set; }
    public int SerialNo { get; set; }
    public int RecordsCount { get; set; }
    public long RowNumber { get; set; }
    public string RowDisplay => $"{RowNumber} / {RecordsCount}";
}