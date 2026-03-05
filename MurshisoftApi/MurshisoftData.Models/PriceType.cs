using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MurshisoftData.Models
{
   
     public enum PriceType { None=0,SalesPrice = 1, CostPrice = 2 }

    public static class PriceTypes
    {
        public static PriceType GetPriceType(int VoucherTypeID)
        {
            switch (VoucherTypeID)
            {
                case 4:
                case 5:
                case 7:
                case 9:
                case 10:
                case 11:
                case 14:
                case 15:

                    return PriceType.CostPrice;
                case 6:
                case 8:
                case 12:
                case 13:
                case 16:
                case 18:
                    return PriceType.SalesPrice;
                default:
                    return PriceType.SalesPrice;
            }
        }
    }
}
