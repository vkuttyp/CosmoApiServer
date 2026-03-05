using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace MurshisoftData.Models
{
    public class User
    {
        public int UserID { get; set; }
        public string UserName { get; set; }
        public bool IsAdmin { get; set; }
        public int BranchID { get; set; }
    }

    public class LoginData
    {
        public string UserName { get; set; }
        public string Password { get; set; }
        public string PcName { get; set; }
        public string AppName { get; set; }
    }

    public class SessionRecord
    {
        public int UserID { get; set; }
        public string UserName { get; set; }
        public int GroupID { get; set; }
        public bool IsAdmin { get; set; }
        public int BranchID { get; set; }
        public int FinancialYear { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int PosType { get; set; }
        public bool IsPharmacy { get; set; }
        public string CashAccountNo { get; set; }
        public string BankAccountNo { get; set; }
        public bool SalesPriceTaxInclusive { get; set; }
        public int BranchesCount { get; set; }
        public int LoginId { get; set; }
        public bool ZatcaEnabled { get; set; }
        public int BinFadel { get; set; }
        public int PrintInvoice { get; set; }
        public TaxType TaxType { get; set; }
        public EmailSetting EmailSetting { get; set; }
        public PosSettings PosSettings { get; set; }
        public CompanyInfo CompanyInfo { get; set; }
        public Psettings PSettings { get; set; }
        public Setting[] Settings { get; set; }
        public ProgramOption[] ProgramOptions { get; set; }
        public Programsetting[] ProgramSettings { get; set; }
    }
    public class Psettings
    {
        public int Id0 { get; set; }
        public int Id1 { get; set; }
        public int Id2 { get; set; }
        public int Id3 { get; set; }
        public int Id4 { get; set; }
        public int Id8 { get; set; }
    }
    public class SessionData
    {
        public SessionData(int userID, string userName, int groupID, bool isAdmin, int branchID, int financialYear, DateTime fromDate, DateTime toDate, int posType, bool isPharmacy, string cashAccountNo, string bankAccountNo, bool salesPriceTaxInclusive, int branchesCount, int loginId, bool zatcaEnabled, TaxType taxType, EmailSetting emailSetting, PosSettings posSettings, CompanyInfo companyInfo, int binFadel, int printInvoice, Psettings psettings)
        {
            UserID = userID;
            UserName = userName;
            GroupID = groupID;
            IsAdmin = isAdmin;
            BranchID = branchID;
            FinancialYear = financialYear;
            FromDate = fromDate;
            ToDate = toDate;
            PosType = posType;
            IsPharmacy = isPharmacy;
            CashAccountNo = cashAccountNo;
            BankAccountNo = bankAccountNo;
            SalesPriceTaxInclusive = salesPriceTaxInclusive;
            BranchesCount = branchesCount;
            LoginId = loginId;
            ZatcaEnabled = zatcaEnabled;
            TaxType = taxType;
            EmailSetting = emailSetting;
            PosSettings = posSettings;
            CompanyInfo = companyInfo;
            BinFadel = binFadel;
            PrintInvoice = printInvoice;
            PSettings= psettings;
        }

        public int UserID { get; set; }
        public string UserName { get; set; }
        public int GroupID { get; set; }
        public bool IsAdmin { get; set; }
        public int BranchID { get; set; }
        public int FinancialYear { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int PosType { get; set; }
        public bool IsPharmacy { get; set; }
        public string CashAccountNo { get; set; }
        public string BankAccountNo { get; set; }
        public bool SalesPriceTaxInclusive { get; set; }
        public int BranchesCount { get; set; }
        public int LoginId { get; set; }
        public bool ZatcaEnabled { get; set; }
        public int BinFadel { get; set; }
        public int PrintInvoice { get; set; }
        public TaxType TaxType { get; set; }
        public EmailSetting EmailSetting { get; set; }
        public PosSettings PosSettings { get; set; }
        public CompanyInfo CompanyInfo { get; set; }
        public Psettings PSettings { get; set; }
    }

    public class Setting
    {
        public int id { get; set; }
        public int SettingValue { get; set; }
    }

    public class ProgramOption
    {
        public int OptionID { get; set; }
        public string OptionValue { get; set; }
    }

    public class Programsetting
    {
        public int SettingId { get; set; }
        public string SettingName { get; set; }
        public int SettingValue { get; set; }
        public string StringValue { get; set; }
    }

    public class CompanyInfo
    {
        public int BranchID { get; set; }
        public string ACompanyName { get; set; }
        public string ALine1 { get; set; }
        public string ALine2 { get; set; }
        public string ALine3 { get; set; }
        public string ALine4 { get; set; }
        public string ECompanyName { get; set; }
        public string ELine1 { get; set; }
        public string ELine2 { get; set; }
        public string ELine3 { get; set; }
        public string ELine4 { get; set; }
        public string SponsorNo { get; set; }
        public string SponsorName { get; set; }
        public string SponsorAddress { get; set; }
        public string SponsorTel { get; set; }
        public byte[] CompanyLogo { get; set; }
    }
    public class PosSettings
    {
        public int PosType { get; set; }
        public bool ShowNewItemWindow { get; set; }
        public bool EnableLock { get; set; }
        public int DiscountType { get; set; }
        public int InvoiceCopies { get; set; }
        public int HasDepartmentPrinters { get; set; }
        public int MultiUnit { get; set; }
        public string UnlockSalesEditPassword { get; set; }
        public string CashierPrinter { get; set; }
        public string KitchenPrinter { get; set; }
        public string CustomerMainAccount { get; set; }
        public string AdminPassword { get; set; }
        public string CashDrawerCode { get; set; }
        public bool EnableSharing { get; set; }
        public int ShowColorCombo { get; set; }
        public int ShowExpiry { get; set; }
        public int SellMinusQuantity { get; set; }
        public int WorkshopSystem { get; set; }
        public int ShowDuplicate {  get; set; }
        public int ItemAutoNumbering { get; set; }
        public int ShowPriceTypesInSales { get; set; }
        public int CreditLimit { get; set; }
        public LessPriceAuth LessPriceAuth { get; set; }
    }
    public class EmailSetting
    {
        public int Enabled { get; set; }
        public string ToAddress { get; set; }
        public string SenderName { get; set; }
    }
    public class LessPriceAuth
    {
        public int Enabled { get; set; }
        public string OpenPassword { get; set; }
        public string Password { get; set; }
        public int LessthanCostPrice { get; set; }
        public int LessthanLastPrice { get; set; }
        public string LessthanCostPricePassword { get; set; }
        public string LessthanLastPricePassword { get; set; }
    }
    public class TaxType : IEquatable<TaxType>
    {
        public int id { get; set; }
        public string name { get; set; }
        public decimal TaxPercent { get; set; }
        public string CategoryCode { get; set; } = "S";
        public string ReasonCode { get; set; } = "";
        public string Description { get; set; } = "";
        bool IEquatable<TaxType>.Equals(TaxType other)
        {
            if (other == null) return false;
            return (this.id == other.id);
        }
        [JsonIgnore]
        public TaxType Self => this;
    }
   
}
