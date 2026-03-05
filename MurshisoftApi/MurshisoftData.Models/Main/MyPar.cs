using MurshisoftData.Models;
using System;
using System.Collections.Generic;

namespace MurshisoftData.Main;

public class AcStatementPar
{
    public string Language { get; set; }
    public int VoucherTypeId { get; set; }
    public int FinancialYear { get; set; }
    public int BranchId { get; set; }
    public string AccountNo { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}
    public class ContractItem
{
    public string ItemID { get; set; }
    public string ItemName { get; set; }
    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalesPrice { get; set; }
    public decimal TaxPercent { get; set; }
}
public class VoucherSearchPar
{
    public int VoucherTypeId { get; set; }
    public int BranchId { get; set; }
    public int FinancialYear { get; set; }
    public string BillTo { get; set; } = "";
    public string Details { get; set; } = "";
    public string Details2 { get; set; } = "";
    public string ChequeNo { get; set; } = "";
    public string OtherNo { get; set; } = "";
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public int CostCenterId { get; set; }
}
public class TransFormPar
{
    public int VoucherTypeId { get; set; }
    public int BranchId { get; set; }
    public string AccountNo { get; set; }
    public string DocumentNo { get; set; }
    public string TransIdToEdit { get; set; }
    public List<ItemTurnover> OrderItems { get; set; }
    public List<ScanLineItem> ScannerItems { get; set; }
    public int TransferToBranch { get; set; }
    public ReturnInvoiceData ReturnInvoiceData { get; set; }
}
public class ScanLineItem
{
    public int id { get; set; }
    public int StockTakeID { get; set; }
    public string ItemID { get; set; }
    public string ItemName { get; set; }
    public string Barcode { get; set; }
    public decimal Quantity { get; set; }
    public decimal NumOfPieces { get; set; } = 1;
    public decimal Balance { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalesPrice { get; set; }
    public DateTime ExpiryDate { get; set; } = DateTime.Today;
    public decimal Difference
    {
        get { return Quantity - Balance; }
    }
    public decimal TaxPercent { get; set; }
}
public class ItemTurnover
{
    public string ItemID { get; set; }
    public string ItemName { get; set; }
    public decimal LastPurchasePrice { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal Balance { get; set; }
    public decimal OrderQuantity { get; set; }
}
public class ReturnInvoiceData
{
    public TransactionMain Transaction { get; set; }
    public List<ReturnItem> ReturnItems { get; set; }
}
public class ReturnItem
{
    public bool Selected { get; set; }
    public TransactionDetail Line { get; set; }
    public string ItemId => Line?.ItemID ?? "";
    public string ItemName => Line?.ItemName ?? "";
    public decimal Quantity => Line?.Quantity ?? 0;
    public decimal QuantityToReturn { get; set; }
}
public class SearchPar
{
    public string TransactionId { get; set; }
    public string NumOnly { get; set; }
    public string CustomerId { get; set; }
    public bool IsTemp { get; set; }
    public bool LineItemsOnly { get; set; }
    public bool IsSalesFromReserve { get; set; }
    public bool IsFash { get; set; }
    public bool IsReturn { get; set; }
    public int VoucherTypeId { get; set; }
    public string Search { get; set; } = "";
    public int FinancialYear { get; set; }
    public int BranchId { get; set; }
    public int RepId { get; set; }

}