using System;

namespace MurshisoftData.Models;

public class Account
{
    public string AccountNo { get; set; } = "";
    public string RefNo { get; set; }
    public string AccountName { get; set; }
    public int AccountTypeID { get; set; }
    public string Address { get; set; }
    public string IDNo { get; set; }
    public string MobileNo { get; set; }
    public string TelNo { get; set; }
    public string FaxNo { get; set; }
    public string ContactPerson { get; set; }
    public bool IsMain { get; set; }
    public string UserName { get; set; }
    public DateTime AddDate { get; set; }
    public bool WholeSale { get; set; }
    public decimal CreditLimit { get; set; }
    public int AccountNoInt { get; set; }
    public int RefNoInt { get; set; }
    public string AccountNameEnglish { get; set; }
    public int CreditPeriod { get; set; }
    public int StatusID { get; set; }
    public Transtotal TransTotal { get; set; }
    public Postaladdress PostalAddress { get; set; }
}

public class Transtotal
{
    public decimal Balance { get; set; }
}

public class Postaladdress
{
    public int id { get; set; }
    public string CustomerId { get; set; }
    public string BuildingNumber { get; set; }
    public string PlotIdentification { get; set; }
    public string StreetName { get; set; }
    public string AdditionalStreetName { get; set; }
    public string PostalZone { get; set; }
    public string CityName { get; set; }
    public string CountrySubentity { get; set; }
    public string CitySubdivisionName { get; set; }
    public string Country { get; set; }
    public string ShortAddress { get; set; }
}
