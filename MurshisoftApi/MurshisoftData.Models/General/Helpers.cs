using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;

namespace MurshisoftData.Models;

public static class MyHelpers
{
    public static bool SqlLike(string strMain, string strSub)
    {
        return strMain.IndexOf(strSub, StringComparison.OrdinalIgnoreCase) >= 0;

    }
    public static  bool SqlEq(decimal strMain, string strSub)
    {
        if (decimal.TryParse(strSub, out var amnt))
        {
            return strMain == amnt;
        }
        return true;
    }
    public static bool TryGet(IDataRecord dr, int ordinal)
    {
        try
        {
            return dr.GetBoolean(ordinal);
        }
        catch
        {
            return false;
        }
    }
    public static int TryGetInt(IDataRecord dr, int ordinal)
    {
        try
        {
            return dr.GetInt32(ordinal);
        }
        catch
        {
            return 0;
        }
    }
    public static decimal TryGetDecimal(IDataRecord dr, int ordinal)
    {
        try
        {
            return dr.GetDecimal(ordinal);
        }
        catch
        {
            return 0;
        }
    }
    public static string TryGetString(IDataRecord dr, int ordinal)
    {
        try
        {
            return dr.GetString(ordinal);
        }
        catch
        {
            return "";
        }
    }
    public static decimal Round(decimal value, int digits = 2)
    {
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }
    public static string GetReturnBarcode(string transactionId)
    {
        return transactionId.Replace('-', '.').Replace('/', '.').Replace('C', '0').Replace('T', '2');
    }
    public static string ConvertToHijri2(DateTime dt)
    {
        try
        {
            CultureInfo ci = new CultureInfo("ar-SA");
            ci.DateTimeFormat.Calendar = new UmAlQuraCalendar();
            return dt.ToString("dd/MM/yyyy", ci.DateTimeFormat).Replace(" ", string.Empty);
        }
        catch
        {
            return string.Empty;
        }
    }
}