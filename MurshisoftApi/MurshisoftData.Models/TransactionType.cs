using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Text.Json.Serialization;

namespace MurshisoftData.Models;
public enum TransactionEntryMode
{
    Inward
        , Outward
}
[AddINotifyPropertyChangedInterface]
public class TransactionType
{
    public static string Language = "Arabic";
    public static TaxType ZeroTax;
    public static TaxType StandardTax;

    public string TransactionTypeID { get; set; } = "";
    public string TransactionTypeName { get; set; } = "";
    public PriceType PriceType { get; set; }
    public Color BackColor { get; set; }
    public bool FooterVisible { get; set; }
    public TaxType? TaxType { get; set; }
    public TransactionEntryMode TransactionEntryMode { get; set; }
    private bool arabic = Language == "Arabic" ? true : false;
    public TransactionType(int voucherTypeID)
    {
        switch (voucherTypeID)
        {
            case 4:
                this.TransactionTypeID = "YearlyStockTake";
                this.TransactionTypeName = arabic ? "الجرد" : "Stock Taking";
                this.PriceType = PriceType.CostPrice;
                this.BackColor = Color.Gainsboro;
                this.FooterVisible = false;
                this.TransactionEntryMode = TransactionEntryMode.Inward;
                this.TaxType = ZeroTax;
                break;

            case 5:
                this.TransactionTypeID = "Purchase";
                this.TransactionTypeName = arabic ? "المشتريات" : "Purchase";
                this.PriceType = PriceType.CostPrice;
                //this.BackColor = ColorTranslator.FromHtml("#3BB7DD");
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Inward;
                this.TaxType = StandardTax;
                break;
            case 6:
                this.TransactionTypeID = "Sales";
                this.TransactionTypeName = arabic ? "المبيعات" : "Sales";
                this.PriceType = PriceType.SalesPrice;
                //this.BackColor = ColorTranslator.FromHtml("#C7D9EF");
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = StandardTax;
                break;
            case 7:
                this.TransactionTypeID = "PurchaseReturn";
                this.TransactionTypeName = arabic ? "مردود مشتريات" : "Purchase Return";
                this.PriceType = PriceType.CostPrice;
                //this.BackColor = ColorTranslator.FromHtml("#F6F3EC");
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = StandardTax;
                break;
            case 8:
                this.TransactionTypeID = "CreditAdvice";
                this.TransactionTypeName = arabic ? "الإشعار الدائن" : "Credit Advice";
                this.PriceType = PriceType.SalesPrice;
                this.BackColor = Color.MediumOrchid;
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Inward;
                this.TaxType = StandardTax;
                break;
            case 9:
                this.TransactionTypeID = "TransferBetweenBranches";
                this.TransactionTypeName = arabic ? "تحويلات بين الفروع" : "Branch Transfer";
                this.PriceType = PriceType.CostPrice;
                this.BackColor = Color.Gainsboro;
                this.FooterVisible = false;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = ZeroTax;
                break;
            case 10:
                this.TransactionTypeID = "DamagedStock";
                this.TransactionTypeName = arabic ? "أصناف تالفة" : "Damaged Stock";
                this.PriceType = PriceType.CostPrice;
                this.BackColor = Color.Gainsboro;
                this.FooterVisible = false;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = ZeroTax;
                break;
            case 11:
                this.TransactionTypeID = "TransferRequest";
                this.TransactionTypeName = arabic ? "طلب تحويل اصناف  " : "Item Transfer request";
                this.PriceType = PriceType.CostPrice;
                this.BackColor = Color.Gainsboro;
                this.FooterVisible = false;
                this.TransactionEntryMode = TransactionEntryMode.Inward;
                this.TaxType = ZeroTax;
                break;
            //case 12:
            //    this.TransactionTypeID = "AdvancePayment";
            //    this.TransactionTypeName = arabic ? "فاتورة الدفعة المقدمة" : "Advance Payment Invoice";
            //    this.PriceType = PriceType.SalesPrice;
            //    this.BackColor = Color.LemonChiffon;
            //    this.FooterVisible = true;
            //    this.TransactionEntryMode = TransactionEntryMode.Outward;
            //    this.TaxType = StandardTax;
            //    break;
            case 13:
                this.TransactionTypeID = "DebitAdvice";
                this.TransactionTypeName = arabic ? "الإشعار المدين" : "Debit Advice";
                this.PriceType = PriceType.SalesPrice;
                //this.BackColor = ColorTranslator.FromHtml("#C7D9EF");
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = StandardTax;
                break;

            case 14:
                this.TransactionTypeID = "Production";
                this.TransactionTypeName = arabic ? "الإنتاج" : "Production";
                this.PriceType = PriceType.CostPrice;
                this.BackColor = Color.Bisque;
                this.FooterVisible = false;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = ZeroTax;
                break;

            case 15:
                this.TransactionTypeID = "Orders";
                this.TransactionTypeName = arabic ? "طلبية" : "Order";
                this.PriceType = PriceType.CostPrice;
                this.BackColor = Color.Gainsboro;
                this.FooterVisible = false;
                this.TransactionEntryMode = TransactionEntryMode.Inward;
                this.TaxType = ZeroTax;
                break;
            case 16:
                this.TransactionTypeID = "Quatations";
                this.TransactionTypeName = arabic ? "عرض اسعار" : "Quatation";
                this.PriceType = PriceType.SalesPrice;
                this.BackColor = Color.Gainsboro;
                this.FooterVisible = false;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = StandardTax;
                break;
            case 18:
                this.TransactionTypeID = "ReserveItems";
                this.TransactionTypeName = arabic ? "حجز أصناف" : "Item Reservation";
                this.PriceType = PriceType.SalesPrice;
                this.BackColor = Color.Gainsboro;
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = ZeroTax;
                break;

            case 19:
                this.TransactionTypeID = "KitchenItems";
                this.TransactionTypeName = arabic ? "صرف للمطبخ" : "Items for Kitchen";
                this.PriceType = PriceType.CostPrice;
                this.BackColor = Color.Gainsboro;
                this.FooterVisible = false;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = ZeroTax;
                break;

            case 22:
                this.TransactionTypeID = "StockAdjustment";
                this.TransactionTypeName = arabic ? "تسوية المخزون" : "Stock Adjustment";
                this.PriceType = PriceType.CostPrice;
                this.BackColor = Color.Gainsboro;
                this.FooterVisible = false;
                this.TransactionEntryMode = TransactionEntryMode.Inward;
                this.TaxType = ZeroTax;
                break;

            case 26:
                this.TransactionTypeID = "AdvancePayment";
                this.TransactionTypeName = arabic ? "فاتورة الدفعة المقدمة" : "Advance Payment Invoice";
                this.PriceType = PriceType.SalesPrice;
                this.BackColor = Color.LemonChiffon;
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = StandardTax;
                break;

            case 27:
                this.TransactionTypeID = "AdvancePaymentReturn";
                this.TransactionTypeName = arabic ? "فاتورة الدفعة المقدمة" : "Advance Payment Return Invoice";
                this.PriceType = PriceType.SalesPrice;
                this.BackColor = Color.LemonChiffon;
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = StandardTax;
                break;

            case 28:
                this.TransactionTypeID = "Mustakhlas";
                this.TransactionTypeName = arabic ? "مستخلص" : "Mustakhlas";
                this.PriceType = PriceType.SalesPrice;
                this.BackColor = Color.LemonChiffon;
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = StandardTax;
                break;

            case 29:
                this.TransactionTypeID = "MustakhlasReturn";
                this.TransactionTypeName = arabic ? "مرتجع المستخلص" : "Mustakhlas Return";
                this.PriceType = PriceType.SalesPrice;
                this.BackColor = Color.LemonChiffon;
                this.FooterVisible = true;
                this.TransactionEntryMode = TransactionEntryMode.Outward;
                this.TaxType = StandardTax;
                break;


        }
    }
}