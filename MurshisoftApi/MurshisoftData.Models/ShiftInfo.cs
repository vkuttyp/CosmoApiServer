using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MurshisoftData.Models
{
    public class ShiftInfo
    {
        public int LoginID { get; set; }
        public int ShiftID { get; set; }
        public string ShiftName { get; set; }
        public string FromTime { get; set; }
        public string ToTime { get; set; }
        public int CounterID { get; set; }
        public string CounterName { get; set; }
        public DateTime LoginTime { get; set; }
    }
    public class POSLock
    {
        public string CashierUserName { get; set; }
        public string LockingUserName { get; set; }
        public int LockStatus { get; set; }
        public int ResponseId { get; set; }
        public bool ForceLock { get; set; }
    }
    public class SalesCustomer
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public int CustomerTypeId { get; set; }
        public string Address { get; set; } = "";
        public string MobileNo { get; set; } = "";
        public string EmailId { get; set; } = "";
        public string AccountNo { get; set; } = "";
        public decimal Points { get; set; }
        public decimal DiscountPercent { get; set; }
    }
}
