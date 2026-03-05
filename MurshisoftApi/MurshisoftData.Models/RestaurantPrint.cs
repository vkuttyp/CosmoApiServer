//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace MurshisoftPOS.Models
//{
//    public class PrintMaster
//    {
//        public string TransactionID { get; set; }
//        public int DailySerial { get; set; }
//        public DateTime TransactionDate { get; set; }
//        public DateTime TransactionTime { get; set; }
//        public int BranchID { get; set; }
//        public string BranchName { get; set; }
//        public CompanyInfo CompanyInfo { get; set; }
//        public string Caption { get; set; }
//        public string PaymentMethod { get; set; }
//        public string Notes { get; set; }
//        public decimal TotalAmount { get; set; }
//        public decimal Discount { get; set; }
//        public decimal NetAmount { get; set; }
//        public decimal CashAmount { get; set; }
//        public decimal BalanceAmount { get; set; }
//        public decimal CashAmount2 { get; set; }
//        public decimal CreditAmount { get; set; }
//        public byte[] InvoiceBarcode { get; set; }
//        public List<LineItem> LineItems { get; set; }
//        public List<Printer> Printers { get; set; }
//    }

//    public class Printer
//    {
//        public int id { get; set; }
//        public string name { get; set; }
//        public string ItemID { get; set; }
//        public List<LineItem> LineItems { get; set; }
//    }
//    public class LineItem
//    {
//        public string ItemID { get; set; }
//        public string ItemName { get; set; }
//        public int SubItemId { get; set; }
//        public decimal SalesPrice { get; set; }
//        public decimal Quantity { get; set; }
//        public decimal SalesPriceTotal { get; set; }
//        public decimal Discount { get; set; }
//        public decimal NetSalesPrice { get; set; }
//    }
//}
