using MurshisoftData.Models.POS;
using PropertyChanged;
using System;
using System.Collections.Generic;

namespace MurshisoftData.Models;
public class StockListWithLastInventory
{
    public int LastInventoryId { get; set; }
    public List<StockItem> Items {get;set;}
}
public class StockWithUpdatedBalance
{
    public int LastInventoryId { get; set; }
    public List<NewBalance> Items { get; set; }
}

public class SpanCommission
{
    public int id { get; set; }
    public string name { get; set; }
    public int PaymentMethodId { get; set; }
    public decimal Commission { get; set; }
    public decimal VatPercent { get; set; }
    public string CommissionAccountNo { get; set; }
    public string VatAccountNo { get; set; }
}
internal class ItemImage
{
    public string ItemId { get; set; }
    public string Path { get; set; }
}
public class VoucherType
{
    public int VoucherTypeId { get; set; }
    public string VoucherTypeName { get; set; } = "";
    public string MenuName { get; set; } = "";
}
public class DepartmentPrinter
{
    public int id { get; set; }
    public string name { get; set; }
    public int BranchID { get; set; } = SessionInfoPOS.SessionData.BranchID;
    public bool Active { get; set; } = true;
    public List<DepartmentPrinterCategory> Categories { get; set; } = new List<DepartmentPrinterCategory>();
}
public class DepartmentPrinterCategory
{
    public int id { get; set; }
    public string ItemID { get; set; }
    public int PrinterId { get; set; }
}
[AddINotifyPropertyChangedInterface]

public class StockItem
{
    public string ItemId { get; set; }
    public string ItemName { get; set; }
    public string PartNo { get; set; }
    public string RefNo { get; set; }
    public string ItemNameEnglish { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalesPrice { get; set; }
    public decimal LastPrice { get; set; }
    public string Location { get; set; }
    public decimal  Balance { get; set; }
    public decimal TaxPercent { get; set; }
    public string BalanceUnit { get; set; }
}
public class ListItem
{
    public ListItem(int id, string name)
    {
        this.id = id;
        this.name = name;
    }

    public int id { get; set; }
    public string name { get; set; }
}
public class XReportModel
{
    public int id { get; set; }
    public string Col1 { get; set; }
    public string Col2 { get; set; }
    public string Col3 { get; set; }
    public decimal Col4 { get; set; }
    public int RowType { get; set; }
}
public class XReportByCategoryModel
{
    public string CategoryID { get; set; }
    public string CategoryName { get; set; }
    public string ItemID { get; set; }
    public string ItemName { get; set; }
    public decimal Quantity { get; set; }
    public decimal SalesPrice { get; set; }
}
public class PosItemPar
{
    public PosItemPar(int financialYear, int branchId, string barcode)
    {
        FinancialYear = financialYear;
        BranchId = branchId;
        Barcode = barcode;
    }

    public int FinancialYear { get; set; }
    public int BranchId { get; set; }
    public string Barcode { get; set; }

}

public class NextIdParam
{
    public NextIdParam(int financialYear, int branchId, int voucherTypeId)
    {
        FinancialYear = financialYear;
        BranchId = branchId;
        VoucherTypeId = voucherTypeId;
    }

    public int FinancialYear { get; set; }
    public int BranchId { get; set; }
    public int VoucherTypeId { get; set; }

}
public class NextIdResult
{
    public NextIdResult(string transactionId, int dayId)
    {
        TransactionId = transactionId;
        DayId = dayId;
    }

    public string TransactionId { get; set; }
    public int DayId { get; set; }
}
public class CreditLimitPar
{
    public CreditLimitPar(string customerId, int financialYear, int branchId, string transactionId, decimal creditRequestAmount)
    {
        CustomerId = customerId;
        FinancialYear = financialYear;
        BranchId = branchId;
        TransactionId = transactionId;
        CreditRequestAmount = creditRequestAmount;
    }

    public string CustomerId { get; set; }
    public int FinancialYear { get; set; }
    public int BranchId { get; set; }
    public string TransactionId { get; set; }
    public decimal CreditRequestAmount { get; set; }
}
// If you prefer not to use records, you can replace the record with a class as follows:

public class InvoiceSearchParam
{
    public DateTime Date { get; set; }
    public string UserName { get; set; }
    public int VoucherTypeId { get; set; }

    public InvoiceSearchParam(DateTime date, string userName, int voucherTypeId)
    {
        Date = date;
        UserName = userName;
        VoucherTypeId = voucherTypeId;
    }
}
public class InvoiceSearchResult
{
    public string TransactionId { get; set; }
    public DateTime TransactionDate { get; set; }
    public int DailySerial { get; set; }
    public decimal NetAmount { get; set; }
}
public class ItemCategory
{
    public ItemCategory(string itemId, string itemName)
    {
        ItemId = itemId;
        ItemName = itemName;
    }

    public string ItemId { get; set; }
    public string ItemName { get; set; }
}
public class BranchBalance
{
    public BranchBalance(int branchId, string branchName, string balance)
    {
        BranchId = branchId;
        BranchName = branchName;
        Balance = balance;
    }

    public int BranchId { get; set; }
    public string BranchName { get; set; }
    public string Balance { get; set; }
}
public class NewBalance
{
    public NewBalance(string itemId, decimal balance, string balanceUnit)
    {
        ItemId = itemId;
        Balance = balance;
        BalanceUnit = balanceUnit;
    }

    public string ItemId { get; set; }
    public decimal Balance { get; set; }
    public string BalanceUnit { get; set; }
}

//public class StockItem
//{
//    public string ItemId { get; set; }
//    public string ItemName { get; set; }
//    public string PartNo { get; set; }
//    public string RefNo { get; set; }
//    public string ItemNameEnglish { get; set; }
//    public decimal CostPrice { get; set; }
//    public decimal SalesPrice { get; set; }
//    public decimal LastPrice { get; set; }
//    public string Location { get; set; }
//}
public class InvoicePrintResult
{
    public InvoicePrintResult(string header, string details, string qrCode)
    {
        Header = header;
        Details = details;
        QrCode = qrCode;
    }

    public string Header { get; set; }
    public string Details { get; set; }
    public string QrCode { get; set; }
}
public class RestInvoicePrintResult
{
    public RestInvoicePrintResult(string table, string qrCode)
    {
        this.table = table;
        QrCode = qrCode;
    }

    public string table { get; set; }
    public string QrCode { get; set; }
}

public class DataTableJson
{
    public DataTableJson(string jsonData)
    {
        JsonData = jsonData;
    }

    public string JsonData { get; set; }
}
// Replace the record with a class to avoid CS0518 and fix property naming for IDE1006
public class CustomerReportPar
{
    public int CustomerId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    public CustomerReportPar(int customerId, DateTime from, DateTime to)
    {
        CustomerId = customerId;
        From = from;
        To = to;
    }
}
public class TempSearchResult
{
    public string TransactionId { get; set; }
    public int NumOnly { get; set; }
    public DateTime TransactionDate { get; set; }
    public decimal NetAmount { get; set; }
    public string ChequeNo { get; set; }
}
public class PosCategory
{
    public string ID { get; set; }
    public string Name { get; set; }
    public List<PosCatItem> Items { get; set; }
}
public class PosCatItem
{
    public string ItemID { get; set; }
    public string ItemName { get; set; }
    public decimal SalesPrice { get; set; }
    public bool TaxInclusive { get; set; }
}
// Replace the record with a class to avoid CS0518 and fix property naming for IDE1006
public class ItemDeletePar
{
    public int BranchId { get; set; }
    public string UserName { get; set; }
    public int VoucherTypeId { get; set; }
    public int CancelType { get; set; }
    public string ItemId { get; set; }
    public decimal Qty { get; set; }
    public decimal NumOfPieces { get; set; }
    public decimal Total { get; set; }

    public ItemDeletePar(int branchId, string userName, int voucherTypeId, int cancelType, string itemId, decimal qty, decimal numOfPieces, decimal total)
    {
        BranchId = branchId;
        UserName = userName;
        VoucherTypeId = voucherTypeId;
        CancelType = cancelType;
        ItemId = itemId;
        Qty = qty;
        NumOfPieces = numOfPieces;
        Total = total;
    }
}

// Replace the record with a class to avoid CS0518 and fix property naming for IDE1006
public class InvoicePrintParam
{
    public string InvoiceNo { get; set; }
    public int FinancialYear { get; set; }
    public string Language { get; set; }

    public InvoicePrintParam(string invoiceNo, int financialYear, string language)
    {
        InvoiceNo = invoiceNo;
        FinancialYear = financialYear;
        Language = language;
    }
}

public class RestInvoiceParm
{
    public string ProcName { get; }
    public string TransactionID { get; }
    public int BranchID { get; }
    public int InvoiceHeadingID { get; }
    public int DepPrinterId { get; }

    public RestInvoiceParm(string procName, string transactionID, int branchID = 0, int invoiceHeadingID = 0, int depPrinterId = 0)
    {
        ProcName = procName;
        TransactionID = transactionID;
        BranchID = branchID;
        InvoiceHeadingID = invoiceHeadingID;
        DepPrinterId = depPrinterId;
    }
    public void Deconstruct(out string procName, out string transactionID, out int branchID, out int invoiceHeadingID, out int depPrinterId)
    {
        procName = ProcName;
        transactionID = TransactionID;
        branchID = BranchID;
        invoiceHeadingID = InvoiceHeadingID;
        depPrinterId = DepPrinterId;
    }
}

public class TransactioinByIdPar
{
    public string Id { get; }
    public bool IsTemp { get; }
    public bool IsReturn { get; }

    public TransactioinByIdPar(string id, bool isTemp, bool isReturn)
    {
        Id = id;
        IsTemp = isTemp;
        IsReturn = isReturn;
    }


}

public class XReportParam
{
    public DateTime From { get; }
    public DateTime To { get; }
    public string UserName { get; }
    public int BranchId { get; }
    public int ReportType { get; }
    public string TrIdFrom { get; }
    public string TrIdTo { get; }
    public string Language { get; }

    public XReportParam(DateTime from, DateTime to, string userName, int branchId, int reportType, string trIdFrom, string trIdTo, string language)
    {
        From = from;
        To = to;
        UserName = userName;
        BranchId = branchId;
        ReportType = reportType;
        TrIdFrom = trIdFrom;
        TrIdTo = trIdTo;
        Language = language;
    }

}
public class XReportByCategoryParam
{
    public int BranchId { get; }
    public DateTime From { get; }
    public DateTime To { get; }
    public string CategoryId { get; }
    public string TrIdFrom { get; }
    public string TrIdTo { get; }

    public XReportByCategoryParam(int branchId, DateTime from, DateTime to, string categoryId, string trIdFrom, string trIdTo)
    {
        BranchId = branchId;
        From = from;
        To = to;
        CategoryId = categoryId;
        TrIdFrom = trIdFrom;
        TrIdTo = trIdTo;
    }

}
public class CloseSalesParam
{
    public int FinancialYear { get; }
    public int BranchId { get; }
    public int ClosingId { get; }
    public string UserName { get; }
    public decimal Amount { get; }
    public DateTime ClosingDate { get; }
    public DateTime ClosingTime { get; }
    public string Notes { get; }

    public CloseSalesParam(int financialYear, int branchId, int closingId, string userName, decimal amount, DateTime closingDate, DateTime closingTime, string notes = "")
    {
        FinancialYear = financialYear;
        BranchId = branchId;
        ClosingId = closingId;
        UserName = userName;
        Amount = amount;
        ClosingDate = closingDate;
        ClosingTime = closingTime;
        Notes = notes;
    }


}

public class ClosingReportPar
{
    public ClosingReportPar(DateTime from, DateTime to)
    {
        From = from;
        To = to;
    }

    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
public class SyncTransJob
{
    public SyncTransJob(string transactionId)
    {
        TransactionId = transactionId;
    }

    public string TransactionId { get; set; }
}
public class SyncStockJob
{
    public SyncStockJob(string id)
    {
        ItemId = id;
    }

    public string ItemId { get; set; }
}