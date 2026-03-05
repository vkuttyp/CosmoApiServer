
using System;
using System.Collections.Generic;
using System.Linq;

namespace MurshisoftData.Models;

public class Common
{
    public static string CalculateUnitFooter(decimal Quantity, string itemId, List<Unit> units)
    {
        if (string.IsNullOrEmpty(itemId) || Quantity == 0) return "0.00";
        bool isMinus = Quantity < 0;
        Quantity = Math.Abs(Quantity);
        //bool hasMultiUnit = ProgramSetting.GetSettings(false)[16].SettingValue == 1;
        //if (!hasMultiUnit) return Quantity.ToString("f2");
        string output = "";
        //var list = UnitsListByItemID(itemId);
        var count = units?.Count ?? 0;
        if (count == 0 || count == 1) return isMinus ? $"-{Quantity:f2}" : Quantity.ToString("f2");// Quantity.ToString("f2");

        foreach (var t in units.OrderByDescending(a => a.NumOfPieces))
        {
            if (t.NumOfPieces == 0) return isMinus ? $"-{Quantity:f2}" : Quantity.ToString("f2");
            var num = Math.Floor(Quantity / t.NumOfPieces);
            if (num > 0)
            {
                output += $"{num} {t.UnitName} ";
                Quantity = Quantity - (num * t.NumOfPieces);
            }
        }
        return isMinus ? "-" + output : output;
    }
    public static decimal Round(decimal value, int digits = 2)
    {
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }
}
