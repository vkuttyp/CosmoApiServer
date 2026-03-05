using MurshisoftData.Models.POS;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MurshisoftData.Models
{
    [AddINotifyPropertyChangedInterface]
    public class PaymentType
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public int TypeId { get; set; }
        public string AccountNo { get; set; } = "";
        public string AccountName { get; set; } = "";
    }
    [AddINotifyPropertyChangedInterface]
    public class TransactionPayment
    {
        public int id { get; set; }
        public int PaymentTypeId { get; set; }
        public string TransactionId { get; set; } = "";
        public string AccountNo { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string DocumentNo { get; set; } = "";
        public decimal Amount { get; set; }
    }
    [AddINotifyPropertyChangedInterface]
    public class TransactionMain
    {
        public List<TransactionPayment>? Payments { get; set; } = [];// new List<TransactionPayment>();
        public SpanResponseData? SpanResponse { get; set; }
        public string HallName { get; set; } = "";
        public string TableName { get; set; } = "";
        public bool EnteredReturnReason => CounterID > 0;
        public int TaxTypeId { get; set; }
        public decimal CreditLimit { get; set; }
        public bool IsEditMode { get; set; }
        public decimal DiscountPercent { get; set; }
        public string TransactionID { get; set; } = "";
        public DateTime TransactionDate { get; set; } = DateTime.Today;
        public DateTime TransactionTime { get; set; } = DateTime.Now;
        public string DocumentNo { get; set; } = "";
        public int VoucherTypeID { get; set; }
        public string UserName { get; set; } = SessionInfoPOS.SessionData?.UserName ?? "";
        public int BranchID { get; set; } = SessionInfoPOS.SessionData?.BranchID ?? 0;
        public int CostCenterID { get; set; }
        public int TransferToBranchID { get; set; }
        public int RepresentativeID { get; set; } = SessionInfoPOS.SessionData?.UserID ?? 0;
        public string CustomerID { get; set; } = "";
        public string CustomerName { get; set; } = "";
        [AlsoNotifyFor("PaymentMethod")]
        public decimal Discount { get; set; }
        public decimal AmountAfterDiscount => TotalAmount - Discount;
        public decimal RoundValue { get; set; }// ((TotalAmount + TaxAmount) - Discount) - NetAmount;
        public decimal NetAmount { get; set; }
        public decimal CashAmount { get; set; }
        public decimal BalanceAmount => CashAmount2 != 0.0M ? 0.0M : CashAmount - NetAmount;
        public decimal OtherExpenses { get; set; }
        public int FinancialYear { get; set; } = SessionInfoPOS.SessionData?.FinancialYear ?? 0;
        public string ChequeNo { get; set; } = "";
        public int SerialNo { get; set; }
        public string Notes { get; set; } = "";
        public bool IsSalesFromReserve { get; set; }
        public DateTime PaymentDueDate { get; set; } = DateTime.Today;
        public bool Posted { get; set; } = true;
        public int ServeTypeID { get; set; } = 1;
        public int HallID { get; set; }
        public int TableID { get; set; }
        public int DailySerial { get; set; }
        public int RestCustomerID { get; set; }
        public SalesCustomer? SalesCustomer { get; set; }
        public string RestBookTime { get; set; } = "";
        public decimal CashAmount2 { get; set; }
        public decimal CreditAmount { get; set; }
        public string? BankAccountNo { get; set; } = "";
        public int CounterID { get; set; }
        public int PaymentMethod { get; set; }
        public DateTime DeliveryDate { get; set; } = DateTime.Today;
        public TaxType? TaxType { get; set; }
        public decimal TaxPercent { get; set; }
        public decimal TaxAmount { get; set; }
        public string VoucherTypeName { get; set; } = "";
        public string CustomerVat { get; set; } = "";
        public int SpanTypeId { get; set; }
        public string CashAccountNo { get; set; } = "";
        public bool HasAttachment { get; set; }
        public TransactionMain()
        {
            ////var taxTypes = DataAccess.TransactionData.GetTaxTypes();

            ////this.TaxType = taxTypes.FirstOrDefault(a => a.id == 1);

            //if (TransactionDetails != null)
            //{
            //    TransactionDetails.ListChanged -= TransactionDetails_ListChanged;
            //    TransactionDetails.ListChanged += TransactionDetails_ListChanged;
            //}
        }
        public TransactionMain(int voucherTypeID, int branchId)
        {
            VoucherTypeID = voucherTypeID;
            //var taxTypes = DataAccess.TransactionData.GetTaxTypes();
            //this.TaxType = taxTypes.FirstOrDefault(a => a.id == 1);
            //if (TransactionDetails != null)
            //{
            //    TransactionDetails.ListChanged -= TransactionDetails_ListChanged;
            //    TransactionDetails.ListChanged += TransactionDetails_ListChanged;
            //}
            //this.PropertyChanged += new PropertyChangedEventHandler(TransactionMain_PropertyChanged);

        }

        public string TransactionDate2 => "";// CommonFunctions.ConvertToHijri(TransactionDate);
        public decimal TotalAmount { get; set; } //=> SalesPriceTotal;
        //void TransactionDetails_ListChanged(object sender, ListChangedEventArgs e)
        //{
        //    OnPropertyChanged("TotalAmount");
        //}

        public MyList<TransactionDetail> TransactionDetails { get; set; } = new();
        public decimal CostPriceTotal { get; set; } //=> TransactionDetails?.FastSum(a => a.CostPriceTotal) ?? 0;
        public decimal SalesPriceTotal { get; set; } // => TransactionDetails?.FastSum(a => a.SalesPriceTotal) ?? 0;
        public decimal LastPriceTotal { get; set; } // => TransactionDetails?.FastSum(a => a.LastPrice * a.Quantity) ?? 0;
        public decimal DiscountTotal { get; set; } // => TransactionDetails?.FastSum(a => a.Discount) ?? 0;
        public decimal QuantityTotal { get; set; }
        public decimal NetAmountWithTaxLineTotal { get; set; }
        public string TempTransactionID { get; set; } = "";
        public RestCustomer? RestCustomer { get; set; }
        public int DailySerialSafari { get; set; }
        public decimal TotalQuantity => TransactionDetails?.Sum(a => a.Quantity) ?? 0;
        public CustomerInfo? CustomerInfo { get; set; }

    }
    public class CustomerInfo 
    {
        public string CustomerID { get; set; }
        public string CustomerName { get; set; }
        public decimal CreditLimit { get; set; }
        public decimal TotalCredit { get; set; }
        public bool Wholesale { get; set; }
        public int CreditPeriod { get; set; }
        public string CustomerVat { get; set; }
    }
    public class BranchSub
    {
        public int CostCenterID { get; set; }
        public string CostCenterName { get; set; }
        public int BranchID { get; set; }
    }
    public class ReturnReason
    {
        public int id { get; set; }
        public string name { get; set; }
        public int TypeId { get; set; }
    }
}
