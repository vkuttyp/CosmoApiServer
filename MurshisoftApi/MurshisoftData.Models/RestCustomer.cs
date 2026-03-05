using MurshisoftData.Models.POS;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Data;

namespace MurshisoftData.Models
{
    [AddINotifyPropertyChangedInterface]

    public class RestCustomer
    {
        public int CustomerID { get; set; }
        public string CustomerName { get; set; } = "";
        public string CustomerAddress { get; set; } = "";
        public string CustomerTelNo { get; set; } = "";
        public int BranchID { get; set; } = SessionInfoPOS.SessionData.BranchID;
        public string UserName { get; set; } = SessionInfoPOS.SessionData.UserName;
        public DateTime AddDate { get; set; } = DateTime.Today;

    }

    //public class RestCustomerData
    //{
    //    public static int SaveRestCustomer(RestCustomer customer)
    //    {
    //        using (SqlConnection con = new SqlConnection(MySettingsPOS.ConnectionString))
    //        using (SqlCommand cmd = new SqlCommand("RestaurentCustomers_UPDATE", con))
    //        {
    //            cmd.CommandType = CommandType.StoredProcedure;
    //            cmd.Parameters.AddWithValue("@CustomerID", customer.CustomerID);
    //            cmd.Parameters.AddWithValue("@CustomerName", customer.CustomerName);
    //            cmd.Parameters.AddWithValue("@CustomerAddress", customer.CustomerAddress);
    //            cmd.Parameters.AddWithValue("@CustomerTelNo", customer.CustomerTelNo);
    //            cmd.Parameters.AddWithValue("@BranchID", customer.BranchID);
    //            cmd.Parameters.AddWithValue("@UserName", customer.UserName);
    //            con.Open();
    //            var id = cmd.ExecuteScalar();
    //            if (id == null || id == DBNull.Value) return -1;
    //            return (int)id;
    //        }
    //    }

    //    public static List<RestCustomer> GetCustomers()
    //    {
    //        var list = new List<RestCustomer>();
    //        using (SqlConnection con = new SqlConnection(MySettingsPOS.ConnectionString))
    //        using (SqlCommand cmd = new SqlCommand("RestaurentCustomers_SelectByBranchID", con))
    //        {
    //            cmd.CommandType = CommandType.StoredProcedure;
    //            cmd.Parameters.AddWithValue("@BranchID", SessionInfoPOS.SessionData.BranchID);
    //            con.Open();
    //            var reader = cmd.ExecuteReader();
    //            while (reader.Read())
    //            {
    //                var customer = new RestCustomer();
    //                customer.CustomerID = reader.GetInt32(0);
    //                customer.CustomerName = reader.GetString(1);
    //                customer.CustomerAddress = reader.GetString(2);
    //                customer.CustomerTelNo = reader.GetString(3);
    //                customer.UserName = reader.GetString(4);
    //                customer.AddDate = reader.GetDateTime(5);
    //                list.Add(customer);
    //            }
    //            return list;
    //        }
    //    }
    //}
}
