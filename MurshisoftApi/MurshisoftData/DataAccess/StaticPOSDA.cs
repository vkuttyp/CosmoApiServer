using Microsoft.Data.SqlClient;
using MurshisoftData.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace MurshisoftData.DataAccess;

public  class StaticPOSDA
{
    public static async Task<List<ItemCard>> StockSyncGetPending(string conLocal, string itemId, CancellationToken token = default)
    {
        var lst = new List<string>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("StockSyncPending_Update", con);
        if (!string.IsNullOrEmpty(itemId))
            cmd.Parameters.AddWithValue("@ItemId", itemId);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        var pending = await MyCommand.GetJsonSerialized<List<ItemCard>>(reader, token);
        return pending ?? [];
    }
    public static async Task SaveStockToRemote(string conRemote, string conLocal, ItemCard item, CancellationToken token=default)
    {
        string localId = item.ItemID;
        using var con = new SqlConnection(conRemote);
        using var cmd = MyCommand.CmdProc("Stock_UpdateItemJson", con);
        var jsn = MyCommand.ToJson(item);
        cmd.Parameters.AddWithValue("@json", jsn);
        await con.OpenAsync(token);
        var reader=await cmd.ExecuteReaderAsync(token);
        var savedItem = await MyCommand.GetJsonSerialized<ItemCard>(reader, token);
        if(savedItem != null)
        {
            await StockSyncLocalUpdate(conLocal, localId, savedItem.ItemID, token);
        }
    }
    public static async Task StockSyncLocalUpdate(string conLocal, string itemId, string remoteId, CancellationToken token=default)
    {
        var lst = new List<string>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("StockSyncCompleted_Update", con);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@RemoteId", remoteId);
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
    }
    public static async Task<List<TransactionMain>> TransSyncGetPending(string conLocal, string transactionId, CancellationToken token=default)
    {
        var lst = new List<string>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("TransSyncPending_Update", con);
        if(!string.IsNullOrEmpty(transactionId))
        cmd.Parameters.AddWithValue("@TransactionId", transactionId);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        var pending = await MyCommand.GetJsonSerialized<List<TransactionMain>>(reader, token);
        return pending ?? [];
    }
    public static async Task<int> GetTranSerialNo(string conRemote, string transactionId, CancellationToken token)
    {
        using var con = new SqlConnection(conRemote);
        using var cmd = MyCommand.CmdProc("Trans_SerialNoById", con);
        cmd.Parameters.AddWithValue("@TransactionId", transactionId);
        await con.OpenAsync(token);
        var sno=await cmd.ExecuteScalarAsync(token);
        if (sno == null || sno == DBNull.Value) return 0;
        return (int)sno;
    }
    public static async Task SaveTransactionToRemote(string conRemote, string conLocal, TransactionMain transactionMain, CancellationToken token)
    {
        string localId = transactionMain.TransactionID;
        var sno =await GetTranSerialNo(conRemote, localId, token);
        using var con = new SqlConnection(conRemote);
        using var cmd = MyCommand.CmdProc("Transaction_UpdateJson", con);
        if (sno==0)
        {
            transactionMain.TransactionID = "";
            transactionMain.SerialNo = 0;
        }
        else
        {
            transactionMain.SerialNo = sno;
        }
        var jsn = MyCommand.ToJson(transactionMain);
        cmd.Parameters.AddWithValue("@json", jsn);
        cmd.Parameters.AddWithValue("@UpdateMode", true);
        cmd.Parameters.Add("@OutputTransactionID", SqlDbType.NVarChar, 50).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
        var remoteId = cmd.Parameters["@OutputTransactionID"].Value;
        if (remoteId != null || remoteId != DBNull.Value)
        {
            await TransSyncLocalUpdate(conLocal, localId, remoteId!.ToString(), token);
        }
    }
    public static async Task TransSyncLocalUpdate(string conLocal, string transactionId, string remoteId, CancellationToken token)
    {
        var lst = new List<string>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("TransSyncCompleted_Update", con);
        cmd.Parameters.AddWithValue("@TransactionId", transactionId);
        cmd.Parameters.AddWithValue("@RemoteId", remoteId);
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
    }
    public static async Task<List<SalesCustomer>> GetPosCustomers(string conLocal, CancellationToken token = default)
    {
        var lst = new List<SalesCustomer>();
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("SalesCustomers_SelectAll", con);
        cmd.CommandType = CommandType.StoredProcedure;
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var detail = new SalesCustomer
            {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                CustomerTypeId = reader.GetInt32(2),
                Address = reader.GetString(3),
                MobileNo = reader.GetString(4),
                EmailId = reader.GetString(5),
                AccountNo = reader.GetString(6),
                Points = reader.GetDecimal(7)
            };
            lst.Add(detail);
        }
        return lst;
    }
    public static async Task<List<PosCategory>> GetPosCategories(string conLocal, string userName, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("PosItems_json", con);
        cmd.Parameters.AddWithValue("@UserName", userName);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        var json = await MyCommand.GetJson(reader, token);
        var data = JsonSerializer.Deserialize<List<PosCategory>>(json, MyCommand.option);
        return data;
    }
    public static async Task<int> SavePosClosing(string conLocal, PosSalesClosingMaster closing, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("PosSalesClosing_UpdateJson", con);
        cmd.CommandType = CommandType.StoredProcedure;
        var json = JsonSerializer.Serialize(closing, MyCommand.option);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.Add("@ReturnId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
        var id = cmd.Parameters["@ReturnId"].Value;
        if (id == null || id == DBNull.Value) return -1;
        return (int)id;
    }
    public static async Task<List<PosSalesClosingDetail>> OutstandingsByUser(string conLocal, string userName, CancellationToken token = default)
    {
        var lst = new List<PosSalesClosingDetail>();
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("PosSalesClosing_Outstanding", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@UserName", userName);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var detail = new PosSalesClosingDetail
            {
                id = reader.GetInt32(0),
                ClosingId = reader.GetInt32(1),
                TransactionID = reader.GetString(2),
                VoucherTypeId = reader.GetInt32(3),
                VoucherTypeName = reader.GetString(4),
                SerialNo = reader.GetInt32(5),
                SalesTotal = reader.GetDecimal(6),
                Discount = reader.GetDecimal(7),
                NetSales = reader.GetDecimal(8),
                TaxAmount = reader.GetDecimal(9),
                AmountWithTax = reader.GetDecimal(10),
                Cash = reader.GetDecimal(11),
                Span = reader.GetDecimal(12),
                Credit = reader.GetDecimal(13)
            };
            lst.Add(detail);
        }
        return lst;
    }
    public static async Task<DataTableJson> GetPosUsers(string conLocal, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = new SqlCommand("tblUsers_SelectLockingUsers", con);
        cmd.CommandType = CommandType.StoredProcedure;
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        var table = new DataTable();
        table.Load(reader);
        var json = MyCommand.DataTableToJson(table);
        return new DataTableJson(json);
    }
    public static async Task UpdateSpanResponse(string conLocal, SpanResponseData data, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("SpanResponses_Update", con);
        var json = JsonSerializer.Serialize(data);
        cmd.Parameters.AddWithValue("@json", json);
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
    }
    public static async Task<DataTableJson> CustomersSalesReport(string conLocal, CustomerReportPar par, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = new SqlCommand("SalesCustomer_Invoices", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@from", par.From);
        cmd.Parameters.AddWithValue("@to", par.To);
        cmd.Parameters.AddWithValue("@customerId", par.CustomerId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        var table = new DataTable();
        table.Load(reader);
        var json = MyCommand.DataTableToJson(table);
        return new DataTableJson(json);
    }
    public static async Task<int> UpdatePosCustomer(string conLocal, SalesCustomer customer, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("SalesCustomers_Update", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@id", customer.id);
        cmd.Parameters.AddWithValue("@name", customer.name);
        cmd.Parameters.AddWithValue("@CustomerTypeId", customer.CustomerTypeId);
        cmd.Parameters.AddWithValue("@MobileNo", customer.MobileNo);
        cmd.Parameters.AddWithValue("@Address", customer.Address);
        cmd.Parameters.AddWithValue("@EmailId", customer.EmailId);
        cmd.Parameters.AddWithValue("@AccountNo", customer.AccountNo);
        cmd.Parameters.Add("@OutputId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
        var id = cmd.Parameters["@outputId"].Value;
        if (id == null || id == DBNull.Value) return -1;
        return (int)id;
    }
    public static async Task<SalesCustomer?> GetSalesCustomerByMobileNo(string conLocal, string mobileNo, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("SalesCustomers_SelectByMobileNo", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@MobileNo", mobileNo);
        cmd.Parameters.AddWithValue("@WithBalance", true);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        if (await reader.ReadAsync(token))
        {
            return new SalesCustomer
            {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                CustomerTypeId = reader.GetInt32(2),
                Address = reader.GetString(3),
                MobileNo = reader.GetString(4),
                EmailId = reader.GetString(5),
                AccountNo = reader.GetString(6),
                Points = reader.GetDecimal(7),
                DiscountPercent = reader.GetDecimal(8)
            };
        }
        return null;
    }
    public static async Task<POSLock?> GetByCashierUserName(string conLocal, string cashierUserName, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("POSLock_SelectByCashierUser", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@CashierUserName", cashierUserName);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        if (await reader.ReadAsync(token))
        {
            return new POSLock
            {
                CashierUserName = reader.GetString(0),
                LockingUserName = reader.GetString(1),
                LockStatus = reader.GetInt32(2),
                ResponseId = reader.GetInt32(3),
                ForceLock = reader.GetBoolean(4)
            };
        }
        return null;
    }
    public static async Task<List<DepartmentPrinter>?> LoadPrinters(string conLocal, int branchId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("RestaurantPrinters_json", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<DepartmentPrinter>>(reader,token);
    }

    public static async Task<int> SavePrinter(string conLocal, DepartmentPrinter printer, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("RestPrinters_UpdateJson", con);

        cmd.CommandType = CommandType.StoredProcedure;
        var json = JsonSerializer.Serialize(printer);

        cmd.Parameters.AddWithValue("@json", json);
        await con.OpenAsync(token);
        var id = await cmd.ExecuteScalarAsync(token);
        if (id == null || id == DBNull.Value) return -1;
        return (int)id;
    }
    public static async Task<List<RestaurentHall>?> GetHallsAndTables(string conLocal, int branchId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("RestaurantHallsAndTables", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@BranchId", branchId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<RestaurentHall>>(reader,token);
    }
    public static async Task<List<RestaurantCategory>?> GetCategoriesWithAddons(string conLocal, int branchId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        await con.OpenAsync(token);
        using var cmd = new SqlCommand("RestaurantCategoryAddons_json", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        using var reader = await cmd.ExecuteReaderAsync(token);
        var data = await MyCommand.GetJsonSerialized<List<RestaurantCategory>>(reader,token);
        return data;
    }
    public static async Task<List<RestaurantItem>?> LoadMenuItems(string conLocal,int branchId, int providerId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        await con.OpenAsync(token);
        using var cmd = new SqlCommand("RestaurantItem_SelectJson", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@ProviderId", providerId);
        cmd.Parameters.AddWithValue("@BranchId", branchId);
        using var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<RestaurantItem>>(reader,token) ?? [];
    }
    public static async Task<List<RestaurantItem>> GetChildListByRawMaterial(string conLocal, string rawMaterialId, CancellationToken token = default)
    {
        var retVal = new List<RestaurantItem>();
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("RestaurantStockItemsByRawmaterialID", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@RawMaterialID", rawMaterialId);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var stock = new RestaurantItem
            {
                ItemID = reader.GetString(0),
                ItemName = reader.GetString(1),
                SalesPrice = reader.GetDecimal(2),
                RefNo = reader.GetString(3)
            };
            retVal.Add(stock);
        }

        return retVal;
    }
    public static async Task<List<SpanCommission>> GetSpanTypes(string conLocal, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("SpanCommission_Json", con);
        cmd.CommandType = CommandType.StoredProcedure;
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<SpanCommission>>(reader,token) ?? [];
    }
    public static async Task<List<OnlineProvider>> GetProviders(string conLocal, CancellationToken token = default)
    {
        var retVal = new List<OnlineProvider>();
        using var con = new SqlConnection(conLocal);
        var cmd = new SqlCommand("OnlineProviders_Select", con);
        cmd.CommandType = CommandType.StoredProcedure;
        
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var provider = new OnlineProvider
            {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                AccountNo = reader.GetString(2),
                Percentage = reader.GetDecimal(3)
            };
            retVal.Add(provider);
        }

        return retVal;
    }
    public static async Task UpdateCancelledItem(string conLocal, ItemDeletePar par, CancellationToken token = default)
    {
        try
        {
            using var con = new SqlConnection(conLocal);
            using var cmd = new SqlCommand("StockTransaction_CancelsInsertSingle", con);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
            cmd.Parameters.AddWithValue("@UserName", par.UserName);
            cmd.Parameters.AddWithValue("@ItemID", par.ItemId);
            cmd.Parameters.AddWithValue("@Quantity", par.Qty);
            cmd.Parameters.AddWithValue("@NumOfPieces", par.NumOfPieces);
            cmd.Parameters.AddWithValue("@CancelType", par.CancelType);
            cmd.Parameters.AddWithValue("@VoucherTypeID", par.VoucherTypeId);
            cmd.Parameters.AddWithValue("@Amount", par.Total);
            cmd.Parameters.AddWithValue("@CounterID", 0);
            await con.OpenAsync(token);

            await cmd.ExecuteNonQueryAsync(token);

        }
        catch { }
    }
    public static async Task<string?> SaveCategory(string conLocal, ItemCard stock, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("StockCategory_Update", con);
        cmd.Parameters.AddWithValue("@ItemId", stock.ItemID);
        cmd.Parameters.AddWithValue("@ItemName", stock.ItemName);
        cmd.Parameters.AddWithValue("@AssembledProduct", stock.AssembledProduct);
        cmd.Parameters.AddWithValue("@UserName", stock.UserName);
        cmd.Parameters.AddWithValue("@AutoNum", stock.AutoNum);
        await con.OpenAsync(token);
        var id=await cmd.ExecuteScalarAsync(token);
        return id == null || id == DBNull.Value ? null : id.ToString();
    }
    public static async Task<ItemCard?> SaveStock(string conLocal, ItemCard stock, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Stock_UpdateItemJson", con);
        var jsonInput=JsonSerializer.Serialize(stock);
        cmd.Parameters.AddWithValue("@json", jsonInput);
        await con.OpenAsync(token);
        var reader=await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<ItemCard>(reader,token);
    }
    public static async Task<bool> ItemExists(string conLocal, string barcode, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("ItemExists", con);

        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@barcode", barcode);
        cmd.Parameters.Add("@exist", SqlDbType.Bit).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
        var exist = cmd.Parameters["@exist"].Value;
        if (exist == null) return false;
        return (bool)exist;

    }
    public static async Task<ItemCard?> GetStockById(string conLocal, string id, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("Stock_SelectByIDXmlPOS", con);

        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@ItemID", id);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);

        if (await reader.ReadAsync(token))
        {
            if (!reader.IsDBNull(0))
            {
                using (var s = new StringReader(reader.GetString(0)))
                {
                    var atrb = new XmlRootAttribute("Stock");
                    var serialize = new XmlSerializer(typeof(ItemCard), atrb);
                    var stock = serialize.Deserialize(s);
                    return stock == null ? null : stock as ItemCard;
                }
            }
        }
        return null;
    }
    public static async Task<InvoicePrintResult> GetInvoice(string conLocal, InvoicePrintParam par, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("InvoiceHeader", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@TransactionID", par.InvoiceNo);
        cmd.Parameters.AddWithValue("@FinancialYear", par.FinancialYear);
        cmd.Parameters.AddWithValue("@AdminUser", true);
        cmd.Parameters.AddWithValue("@Language", par.Language);
        await con.OpenAsync(token);
        var table = new DataTable("Header");
        using var reader = await cmd.ExecuteReaderAsync(token);
        await Task.Run(() => table.Load(reader),token);
        SerializableDataTable sdt = SerializableDataTable.FromDataTable(table);
        var ser = new Serializer();
        string jsonHeader = ser.SerializeJson(sdt, true);

        using var cmd2 = new SqlCommand("InvoiceLineItems", con);
        cmd2.CommandType = CommandType.StoredProcedure;
        cmd2.Parameters.AddWithValue("@TransactionID", par.InvoiceNo);
        //await con.OpenAsync();
        table = new DataTable("LineItems");
        using var reader2 = await cmd2.ExecuteReaderAsync(token);
        await Task.Run(() => table.Load(reader2),token);
        sdt = SerializableDataTable.FromDataTable(table);
        var jsonDetails = ser.SerializeJson(sdt, true);
        var qrCode = await GetBase64(conLocal, par.InvoiceNo, token);
        return new InvoicePrintResult(jsonHeader, jsonDetails, qrCode);
    }
    public static async Task<RestInvoicePrintResult> RestInvoiceData(string conLocal, RestInvoiceParm par, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand(par.ProcName, con);

        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@TransactionID", par.TransactionID);
        if (par.BranchID != 0)
            cmd.Parameters.AddWithValue("@BranchID", par.BranchID);
        if (par.InvoiceHeadingID != 0)
            cmd.Parameters.AddWithValue("@InvoiceHeadingID", par.InvoiceHeadingID);
        if (par.DepPrinterId != 0)
            cmd.Parameters.AddWithValue("@PrinterId", par.DepPrinterId);
        await con.OpenAsync(token);
        var table = new DataTable("Invoice");
        using var reader = await cmd.ExecuteReaderAsync(token);
        await Task.Run(() => table.Load(reader), token);
        SerializableDataTable sdt = SerializableDataTable.FromDataTable(table);
        var ser = new Serializer();
        string jsonTable = ser.SerializeJson(sdt, true);
        var qrCode = await GetBase64(conLocal, par.TransactionID, token);
        return new RestInvoicePrintResult(jsonTable, qrCode);
    }
    public static async Task<List<DepartmentPrinter>> GetDepartmentPrinters(string conLocal, int branchId, CancellationToken token = default)
    {
        var list = new List<DepartmentPrinter>();
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("Restaurant_PrintersByBranch", con);

        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var printer = new DepartmentPrinter();
            printer.id = reader.GetInt32(0);
            printer.name = reader.GetString(1);
            printer.BranchID = reader.GetInt32(2);
            printer.Active = reader.GetBoolean(3);
            list.Add(printer);
        }
        return list;
    }
    public static async Task<string> GetBase64(string conLocal, string transactionId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("TaxQrCodeData", con);

        cmd.Parameters.AddWithValue("@TransactionId", transactionId);
        await cmd.Connection.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        if (await reader.ReadAsync(token))
        {
            TLVCls tlv = new TLVCls(reader.GetString(0),
                reader.GetString(1),
                reader.GetDateTime(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4));
            return tlv.ToBase64();
        }
        return "";

    }
    public static async Task<StockListWithLastInventory> GetStockList(string conLocal, int financialYear, int branchId, int voucherTypeId, int searchMethod, string language, CancellationToken token = default)
    {
        var lst = new List<StockItem>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("StockList", con);
        cmd.Parameters.AddWithValue("@FinancialYear", financialYear);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@SearchMethod", searchMethod);
        cmd.Parameters.AddWithValue("@Language", language);
        if (voucherTypeId != 0)
            cmd.Parameters.AddWithValue("@VoucherTypeID", voucherTypeId);

        cmd.Parameters.Add("@LastInventoryId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var s = new StockItem();
            s.ItemId = reader.GetString(0);
            s.PartNo = reader.GetString(1);
            s.RefNo = reader.GetString(2);
            s.ItemName = reader.GetString(3);
            s.ItemNameEnglish = reader.GetString(4);
            s.CostPrice = reader.GetDecimal(5);
            s.SalesPrice = reader.GetDecimal(6);
            s.LastPrice = reader.GetDecimal(7);
            s.Location = reader.GetString(8);
            s.Balance=reader.GetDecimal(9);
            s.TaxPercent = reader.GetDecimal(10);
            s.BalanceUnit=reader.GetString(11);
            lst.Add(s);
        }
        var data=new StockListWithLastInventory();
        var iid = cmd.Parameters["@LastInventoryId"].Value;
        int lastId=iid==null || iid==DBNull.Value ? 0 : (int)iid;
        data.Items = lst;
        data.LastInventoryId = lastId;
        return data;
    }
    public static async Task<StockWithUpdatedBalance> GetUpdatedBalance(string conLocal, int financialYear, int branchId, int lastInventoryId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("StockBalanceUpdateChanges", con);

        cmd.Parameters.AddWithValue("@FinancialYear", financialYear);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@LastInventoryId", lastInventoryId);
        cmd.Parameters.Add("@latestId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        var lst = new List<NewBalance>();
        while (await reader.ReadAsync(token))
        {
            lst.Add(new NewBalance(reader.GetString(0), reader.GetDecimal(1), reader.GetString(2)));
        }
        var lid = cmd.Parameters["@latestId"].Value;
        int lastId=lid==null || lid==DBNull.Value ? 0 : (int)lid;
        
        return new StockWithUpdatedBalance { Items = lst, LastInventoryId=lastId };
    }

    public static async Task<List<User>> UsersList(string conLocal,int branchId, int typeId, CancellationToken token = default)
    {
        var lst = new List<User>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("LoginUsersList", con);
        cmd.Parameters.AddWithValue("@BranchId", branchId);
        cmd.Parameters.AddWithValue("@TypeId", typeId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var user = new User();
            user.UserID = reader.GetInt32(0);
            user.UserName = reader.GetString(1);
            user.IsAdmin = reader.GetBoolean(2);
            user.BranchID = reader.GetInt32(3);
            lst.Add(user);
        }
        return lst;
    }
    public static async Task<List<User>> UsersList(string conLocal, CancellationToken token = default)
    {
        return await UsersList(conLocal, 0, 2, token);
    }
    public static async Task LogOff(string conLocal, int loginId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("UserLogoff_insert", con);
        cmd.Parameters.AddWithValue("@LoginID", loginId);
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);

    }
    public static async Task<string> SaveTransaction(string conLocal, TransactionMain transactionMain, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Transaction_UpdateJson", con);
        var jsn = MyCommand.ToJson(transactionMain);
        cmd.Parameters.AddWithValue("@json", jsn);
        cmd.Parameters.AddWithValue("@UpdateMode", true);
        cmd.Parameters.Add("@OutputTransactionID", SqlDbType.NVarChar, 50).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
        var id = cmd.Parameters["@OutputTransactionID"].Value;
        return id?.ToString() ?? "";
    }
    public static async Task<string> SaveTransactionTemp(string conLocal, TransactionMain transactionMain, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("TransactionTemp_UpdateJson", con);
        var jsn = MyCommand.ToJson(transactionMain);
        cmd.Parameters.AddWithValue("@json", jsn);
        cmd.Parameters.AddWithValue("@UpdateMode", true);
        cmd.Parameters.Add("@OutputTransactionID", SqlDbType.NVarChar, 50).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
        var id = cmd.Parameters["@OutputTransactionID"].Value;
        return id?.ToString();
    }
    public static async Task<List<TempSearchResult>> SearchTemp(string conLocal, int branchId, int voucherTypeId, string userName, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("StockTransactionTempSelectByCounter", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@VoucherTypeID", voucherTypeId);
        cmd.Parameters.AddWithValue("@UserName", userName);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        var lst = new List<TempSearchResult>();
        while (await reader.ReadAsync(token))
        {
            var item = new TempSearchResult();
            item.TransactionDate = reader.GetDateTime(0);
            item.TransactionId = reader.GetString(1);
            item.NumOnly = GetInt(reader.GetString(2));
            item.NetAmount = reader.GetDecimal(3);
            item.ChequeNo = reader.GetValue(4) == DBNull.Value ? "" : reader.GetString(4);
            lst.Add(item);
        }
        return lst;
    }
    static int GetInt(string v)
    {
        int.TryParse(v, out var val);
        return val;
    }
    public static async Task<TransactionMain?> SelectTransactionByID(string conLocal, TransactioinByIdPar par, CancellationToken token = default)
    {
        string procName = par.IsTemp == true ? "StockTransactionTempByID_Json" : "StockTransactionByID_Json";
        using var con = new SqlConnection(conLocal);

        await con.OpenAsync(token);
        using var cmd = new SqlCommand(procName, con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@TransactionID", par.Id);
        if (par.IsReturn)
            cmd.Parameters.AddWithValue("@IsReturn", par.IsReturn);
        using var reader = await cmd.ExecuteReaderAsync(token);
        var data = await MyCommand.GetJsonSerialized<TransactionMain>(reader,token);
        if (data == null) return null;
        data.TransactionDetails ??= [];
        if (data.TransactionDetails != null && data.TransactionDetails.Count > 0)
        {
            foreach (var item in data.TransactionDetails)
            {
                if (item?.FooterInfo != null && item.FooterInfo.Units?.Count == 0)
                {
                    string defaultUnitName = "Piece";
                    var unitName = !string.IsNullOrEmpty(item.UnitName) ? item.UnitName : defaultUnitName;
                    item.FooterInfo.Units = new List<Unit> { new Unit { UnitID = 0, UnitName = unitName } };
                }
            }
        }
        return data;
    }

    public static async Task UpdateClearScreen(string conLocal, TransactionMain transactionMain, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("StockTransactionCancels_INSERTJson", con);
        var jsn = JsonSerializer.Serialize(transactionMain);
        cmd.Parameters.AddWithValue("@json", jsn);
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
    }
    public static async Task<SessionData?> UserLogin(string conLocal, LoginData login, CancellationToken token=default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("UserLogin", con);
        cmd.Parameters.AddWithValue("@UserName", login.UserName);
        cmd.Parameters.AddWithValue("@Password", login.Password);
        cmd.Parameters.AddWithValue("@PcName", login.PcName);
        cmd.Parameters.AddWithValue("@AppName", login.AppName);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        var data = await MyCommand.GetJsonSerialized<SessionRecord>(reader,token);
        return ConvertSessionData(data);
    }
    static SessionData? ConvertSessionData(SessionRecord? data)
    {
        if (data != null)
        {
            data.ZatcaEnabled = (data.ProgramSettings.Where(a => a.SettingId == 18).FirstOrDefault()?.SettingValue ?? 0) == 1;
            data.BinFadel = data.ProgramSettings.Where(a => a.SettingId == 26).FirstOrDefault()?.SettingValue ?? 0;
            data.PrintInvoice = data.ProgramSettings.Where(a => a.SettingId == 8).FirstOrDefault()?.SettingValue ?? 0;
            data.PSettings = new Psettings
            {
                Id0= data.ProgramSettings.Where(a => a.SettingId == 0).FirstOrDefault()?.SettingValue ?? 0,
                Id1 = data.ProgramSettings.Where(a => a.SettingId == 1).FirstOrDefault()?.SettingValue ?? 0,
                Id2 = data.ProgramSettings.Where(a => a.SettingId == 2).FirstOrDefault()?.SettingValue ?? 0,
                Id3 = data.ProgramSettings.Where(a => a.SettingId == 3).FirstOrDefault()?.SettingValue ?? 0,
                Id4 = data.ProgramSettings.Where(a => a.SettingId == 4).FirstOrDefault()?.SettingValue ?? 0,
                Id8 = data.ProgramSettings.Where(a => a.SettingId == 8).FirstOrDefault()?.SettingValue ?? 0,

            };
            data.PosSettings = new PosSettings
            {
                PosType = data.Settings[0].SettingValue,
                ShowNewItemWindow = data.Settings[1].SettingValue == 1,
                EnableLock = data.Settings[2].SettingValue == 1,
                DiscountType = data.Settings[3].SettingValue,
                InvoiceCopies = data.Settings[4].SettingValue,
                ShowPriceTypesInSales = data.ProgramSettings.Where(a => a.SettingId == 0).FirstOrDefault()?.SettingValue ?? 0,

                ShowColorCombo = data.ProgramSettings.Where(a => a.SettingId == 11).FirstOrDefault()?.SettingValue ?? 0,
                ShowExpiry = data.ProgramSettings.Where(a => a.SettingId == 13).FirstOrDefault()?.SettingValue ?? 0,
                WorkshopSystem = data.ProgramSettings.Where(a => a.SettingId == 7).FirstOrDefault()?.SettingValue ?? 0,
                ShowDuplicate = data.ProgramSettings.Where(a => a.SettingId == 12).FirstOrDefault()?.SettingValue ?? 0,
                SellMinusQuantity = data.ProgramSettings.Where(a => a.SettingId == 23).FirstOrDefault()?.SettingValue ?? 0,
                ItemAutoNumbering = data.ProgramSettings.Where(a => a.SettingId == 5).FirstOrDefault()?.SettingValue ?? 0,
                CreditLimit = data.ProgramSettings.Where(a => a.SettingId == 17).FirstOrDefault()?.SettingValue ?? 0,
                
                HasDepartmentPrinters = data.ProgramSettings.Where(a => a.SettingId == 22).FirstOrDefault()?.SettingValue ?? 0,
                UnlockSalesEditPassword= data.ProgramOptions.Where(a => a.OptionID == 2).FirstOrDefault()?.OptionValue ?? "",
                MultiUnit = data.ProgramSettings.Where(a => a.SettingId == 16).FirstOrDefault()?.SettingValue ?? 0,
                AdminPassword = data.ProgramOptions.Where(a => a.OptionID == 13).FirstOrDefault()?.OptionValue ?? "",
                CashierPrinter = data.ProgramOptions.Where(a => a.OptionID == 14).FirstOrDefault()?.OptionValue ?? "",
                KitchenPrinter = data.ProgramOptions.Where(a => a.OptionID == 15).FirstOrDefault()?.OptionValue ?? "",
                CashDrawerCode = data.ProgramSettings.Where(a => a.SettingId == 19).FirstOrDefault()?.StringValue ?? "",
                CustomerMainAccount = data.ProgramOptions.Where(a => a.OptionID == 7).FirstOrDefault()?.OptionValue ?? "",
                EnableSharing = (data.ProgramOptions.Where(a => a.OptionID == 16).FirstOrDefault()?.OptionValue ?? "0") == "1",
                LessPriceAuth = new LessPriceAuth
                {
                    Enabled = data.ProgramSettings.Where(a => a.SettingId == 21).FirstOrDefault()?.SettingValue ?? 0,
                    OpenPassword = data.ProgramSettings.Where(a => a.SettingId == 21).FirstOrDefault()?.StringValue ?? "",
                    Password = data.ProgramSettings.Where(a => a.SettingId == 10).FirstOrDefault()?.StringValue ?? "",
                    LessthanLastPrice = data.ProgramSettings.Where(a => a.SettingId == 9).FirstOrDefault()?.SettingValue ?? 0,
                    LessthanCostPrice = data.ProgramSettings.Where(a => a.SettingId == 10).FirstOrDefault()?.SettingValue ?? 0,
                    LessthanLastPricePassword = data.ProgramSettings.Where(a => a.SettingId == 9).FirstOrDefault()?.StringValue ?? "",
                    LessthanCostPricePassword = data.ProgramSettings.Where(a => a.SettingId == 10).FirstOrDefault()?.StringValue ?? "",

                },
               
            };
            var mail = data.ProgramSettings.Where(a => a.SettingId == 25)?.FirstOrDefault();
            data.EmailSetting = new EmailSetting
            {
                Enabled = mail?.SettingValue ?? 0,
                SenderName = mail?.SettingName ?? "",
                ToAddress = mail?.StringValue ?? ""
            };
            return new SessionData(
            data.UserID,
            data.UserName,
            data.GroupID,
            data.IsAdmin,
            data.BranchID,
            data.FinancialYear,
            data.FromDate,
            data.ToDate,
            data.PosType,
            data.IsPharmacy,
            data.CashAccountNo,
            data.BankAccountNo,
            data.SalesPriceTaxInclusive,
            data.BranchesCount,
            data.LoginId,
            data.ZatcaEnabled,
            data.TaxType,
            data.EmailSetting,
            data.PosSettings,
            data.CompanyInfo,
            data.BinFadel,
            data.PrintInvoice,
            data.PSettings
        );
        }
        return null;
    }
    public static async Task<NextIdResult> GetNextIds(string conLocal, NextIdParam par, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("NextIdsRetrieval", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@FinancialYear", par.FinancialYear);
        cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
        cmd.Parameters.AddWithValue("@VoucherTypeID", par.VoucherTypeId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        if (await reader.ReadAsync(token))
        {
            var trId = reader.GetString(0);
            var nextId = reader.GetInt32(1);
            return new NextIdResult(trId, nextId);
        }
        return new NextIdResult("", 0);
    }

    public static async Task<SalesDetail> GetSalesByLogin(string conLocal, int loginId, CancellationToken token = default)
    {
        using (SqlConnection con = new SqlConnection(conLocal))
        using (SqlCommand cmd = new SqlCommand("PosGetSalesCashByLogin", con))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@LoginID", loginId);
            await con.OpenAsync(token);
            using var reader = await cmd.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                SalesDetail sales = new SalesDetail();
                sales.Cash = reader.GetDecimal(0);
                sales.Paid = reader.GetDecimal(1);
                sales.Sarraf = reader.GetDecimal(2);
                sales.Credit = reader.GetDecimal(3);

                return sales;
            }
        }
        return null;
    }

    private static async Task<PosItemDetail> GetFromReader(SqlDataReader reader, CancellationToken token = default)
    {
        if (await reader.ReadAsync(token))
        {
            var item = new PosItemDetail();
            item.UnitID = reader.GetInt32(0);
            item.ItemID = reader.GetString(1);
            item.ItemName = reader.GetString(2);
            item.UnitName = reader.GetString(3);
            item.NumOfPieces = reader.GetDecimal(4);
            item.CostPrice = reader.GetDecimal(5);
            item.SalesPrice = reader.GetDecimal(6);
            item.LastPrice = reader.GetDecimal(7);
            item.WholeSalePrice = reader.GetDecimal(8);
            item.DiscountAmount = reader.GetDecimal(9);
            item.Barcode = reader.GetString(10);
            item.ExpiryDate = reader.GetDateTime(11);
            item.ItemNameEnglish = reader.GetString(12);
            item.TaxPercent = reader.GetDecimal(13);
            item.DiscountPercent = reader.GetDecimal(14);
            item.RackID = "";
            item.Dose = "";
            item.TaxInclusive = reader.GetBoolean(15);
            return item;
        }
        return null;
    }
    private static  async Task<List<PosItemDetail>> GetFromReaderMultiple(SqlDataReader reader, CancellationToken token = default)
    {
        var list = new List<PosItemDetail>();
        while (await reader.ReadAsync(token))
        {
            var item = new PosItemDetail();
            item.UnitID = reader.GetInt32(0);
            item.ItemID = reader.GetString(1);
            item.ItemName = reader.GetString(2);
            item.UnitName = reader.GetString(3);
            item.NumOfPieces = reader.GetDecimal(4);
            item.CostPrice = reader.GetDecimal(5);
            item.SalesPrice = reader.GetDecimal(6);
            item.LastPrice = reader.GetDecimal(7);
            item.WholeSalePrice = reader.GetDecimal(8);
            item.DiscountAmount = reader.GetDecimal(9);
            item.Barcode = reader.GetString(10);
            item.ExpiryDate = reader.GetDateTime(11);
            item.ItemNameEnglish = reader.GetString(12);
            item.TaxPercent = reader.GetDecimal(13);
            item.DiscountPercent = reader.GetDecimal(14);
            item.RackID = "";
            item.Dose = "";
            item.TaxInclusive = reader.GetBoolean(15);
            list.Add(item);
        }
        return list;
    }


    public  static async Task<List<PosItemDetail>> UnitDetailByBarcode(string conLocal, PosItemPar par, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("UnitDetailByBarcode", con);

        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@Barcode", par.Barcode);
        cmd.Parameters.AddWithValue("@FinancialYear", par.FinancialYear);
        cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await GetFromReaderMultiple(reader,token);
    }

    public static async Task<PosItemDetail> UnitDetailSingleByBarcode(string conLocal, PosItemPar par, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("UnitDetailByBarcode", con);

        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@Barcode", par.Barcode);
        cmd.Parameters.AddWithValue("@FinancialYear", par.FinancialYear);
        cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await GetFromReader(reader);
    }
    public static async Task<PosItemDetail> ItemDettailByBarcodePharmacy(string conLocal, string barcode, decimal discount, decimal discountPercent, int financialYear, int branchId, CancellationToken token = default)
    {
        //Barcode_SelectByID
        PosItemDetail details = null;
        using (var con = new SqlConnection(conLocal))
        {
            await con.OpenAsync(token);
            var cmd = con.CreateCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "PharmacyBarcode_SelectByID";
            cmd.Parameters.AddWithValue("@FinancialYear", financialYear);
            cmd.Parameters.AddWithValue("@BranchID", branchId);
            cmd.Parameters.AddWithValue("@Barcode", barcode);
            var reader = await cmd.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                details = new PosItemDetail();
                details.UnitID = 0;
                details.Barcode = barcode;
                details.ItemID = reader.GetString(1);
                details.ItemName = reader.GetString(2);
                details.UnitName = "";
                details.NumOfPieces = 1.0M;
                details.CostPrice = reader.GetDecimal(3);
                details.SalesPrice = reader.GetDecimal(4);
                details.LastPrice = details.SalesPrice;
                details.WholeSalePrice = details.SalesPrice;
                details.DiscountAmount = discount;
                details.ExpiryDate = reader.GetDateTime(5);
                details.RackID = reader.GetString(6);
                details.Dose = reader.GetString(7);
                details.PreviousExpiryCount = reader.GetDecimal(8);
                details.Balance = reader.GetDecimal(9);
                details.TaxPercent = reader.GetDecimal(10);
            }
        }

        return details;

    }
    public static async Task<List<BarcodeSearchResult>> SearchByBarcode(string conLocal, string barcode, CancellationToken token = default)
    {
        var list = new List<BarcodeSearchResult>();
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("ItemSearchByBarcodeNew", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@Barcode", barcode);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var item = new BarcodeSearchResult();
            item.Barcode = reader.GetString(0);
            item.ItemName = reader.GetString(1);
            item.UnitName = reader.GetString(2);
            item.SalesPrice = reader.GetDecimal(3);
            list.Add(item);
        }
        return list;
    }

    public static async Task<List<Account>?> GetCustomers(string conLocal, int userId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Customers_Listjson", con);
        cmd.Parameters.AddWithValue("@UserId", userId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<Account>>(reader,token);
    }

    public static async Task<bool> IsCreditAllowed(string conLocal, CreditLimitPar par, CancellationToken token = default)
    {
        using (var con = new SqlConnection(conLocal))
        using (var cmd = new SqlCommand("CustomerCreditLimitValidate", con))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@AccountNo", par.CustomerId);
            cmd.Parameters.AddWithValue("@FinancialYear", par.FinancialYear);
            cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
            cmd.Parameters.AddWithValue("@TransactionID", par.TransactionId);
            cmd.Parameters.AddWithValue("@CurrentCreditRequest", par.CreditRequestAmount);
            cmd.Parameters.Add("@HasCreditAvailable", SqlDbType.Bit).Direction = ParameterDirection.Output;
            await con.OpenAsync(token);
            await cmd.ExecuteNonQueryAsync(token);
            var available = cmd.Parameters["@HasCreditAvailable"].Value;
            if (available == null || available == DBNull.Value) return false;
            return (bool)available;

        }
    }
    public static async Task<List<PaymentType>> GetPaymentTypes(string conLocal, int branchId, string language, CancellationToken token = default)
    {
        var _paymentTypes = new List<PaymentType>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("PaymentTypes_List", con);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@Language", language);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            _paymentTypes.Add(new PaymentType
            {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                TypeId = reader.GetInt32(2),
                AccountNo = reader.GetString(3),
                AccountName = reader.GetString(4)
            });
        }
        return _paymentTypes;

    }
    public static async Task<List<InvoiceSearchResult>> SearchInvoices(string conLocal, InvoiceSearchParam par, CancellationToken token = default)
    {
        var lst = new List<InvoiceSearchResult>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("InvoicesByCounterAndUser", con);
        cmd.Parameters.AddWithValue("@date", par.Date);
        cmd.Parameters.AddWithValue("@UserName", par.UserName);
        cmd.Parameters.AddWithValue("@VoucherTypeID", par.VoucherTypeId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            lst.Add(new InvoiceSearchResult
            {
                TransactionId = reader.GetString(0),
                TransactionDate = reader.GetDateTime(1),
                DailySerial = reader.GetInt32(2),
                NetAmount = reader.GetDecimal(3)
            });
        }
        return lst;

    }
    public static async Task<List<ItemCategory>> GetCategories(string conLocal, CancellationToken token = default)
    {
        var lst = new List<ItemCategory>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Categories_List", con);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            lst.Add(new ItemCategory(reader.GetString(0), reader.GetString(1)));
        }
        return lst;
    }

    public static async Task<List<ItemCategory>> GetCategoriesRestaurant(string conLocal, CancellationToken token = default)
    {
        var lst = new List<ItemCategory>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("RestaurantSalesCategories", con);
        cmd.Parameters.AddWithValue("@language", "English");
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            lst.Add(new ItemCategory(reader.GetString(0), reader.GetString(1)));
        }
        return lst;
    }
    public static async Task<List<BranchBalance>> GetBranchBalance(string conLocal, int financialYear, string itemId, CancellationToken token = default)
    {
        var lst = new List<BranchBalance>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("ItemBalanceByBranch", con);
        cmd.Parameters.AddWithValue("@FinancialYear", financialYear);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var b = new BranchBalance(reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
            lst.Add(b);
        }
        return lst;
    }
    public static async Task<(string?, string?)> GetLastIds(string conLocal, int branchId, string userName, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        var cmd = new SqlCommand("PosClosing_LastIds", con);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@User", userName);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        if (await reader.ReadAsync(token))
        {
            var from = reader.GetString(0);
            var to = reader.GetString(1);
            return (from, to);
        }
        return (null, null);
    }
    public static async Task<List<XReportModel>> XReport(string conLocal, XReportParam par, CancellationToken token = default)
    {
        var lst = new List<XReportModel>();
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("XReport", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@ReportType", par.ReportType);
        cmd.Parameters.AddWithValue("@Date1", par.From);
        cmd.Parameters.AddWithValue("@Date2", par.To);
        cmd.Parameters.AddWithValue("@UserName", par.UserName);
        cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
        cmd.Parameters.AddWithValue("@Language", par.Language);
        cmd.Parameters.AddWithValue("@trIdFrom", par.TrIdFrom);
        cmd.Parameters.AddWithValue("@trIdTo", par.TrIdTo);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var s = new XReportModel();
            s.id = reader.GetInt32(0);
            s.Col1 = reader.GetString(1);
            s.Col2 = reader.GetString(2);
            s.Col3 = reader.GetString(3);
            s.Col4 = reader.GetValue(4) == DBNull.Value ? 0 : reader.GetDecimal(4);
            s.RowType = reader.GetInt32(5);
            lst.Add(s);
        }
        return lst;
    }

    public static async Task<List<XReportByCategoryModel>> XReportByCategory(string conLocal, XReportByCategoryParam par, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("XReportByCategories", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
        cmd.Parameters.AddWithValue("@from", par.From);
        cmd.Parameters.AddWithValue("@to", par.To);
        cmd.Parameters.AddWithValue("@trIdFrom", par.TrIdFrom);
        cmd.Parameters.AddWithValue("@trIdTo", par.TrIdTo);
        if (!string.IsNullOrEmpty(par.CategoryId))
            cmd.Parameters.AddWithValue("@CategoryID", par.CategoryId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        var lst = new List<XReportByCategoryModel>();
        while (await reader.ReadAsync(token))
        {
            var item = new XReportByCategoryModel();
            item.CategoryID = reader.GetString(0);
            item.CategoryName = reader.GetString(1);
            item.ItemID = reader.GetString(2);
            item.ItemName = reader.GetString(3);
            item.Quantity = reader.GetDecimal(4);
            item.SalesPrice = reader.GetDecimal(5);
            lst.Add(item);
        }
        return lst;
    }
    public static async Task<ClosingMaster> CloseSales(string conLocal, CloseSalesParam par, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("PosClosing_UpdateJson", con);

        cmd.Parameters.AddWithValue("@ClosingId", par.ClosingId);
        cmd.Parameters.AddWithValue("@FinancialYear", par.FinancialYear);
        cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
        cmd.Parameters.AddWithValue("@ClosingDate", par.ClosingDate);
        cmd.Parameters.AddWithValue("@ClosingTime", par.ClosingTime);
        cmd.Parameters.AddWithValue("@UserName", par.UserName);
        cmd.Parameters.AddWithValue("@ClosingUserName", par.UserName);
        cmd.Parameters.AddWithValue("@UserPaid", par.Amount);
        cmd.Parameters.AddWithValue("@Notes", par.Notes);

        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        var data = await MyCommand.GetJsonSerialized<ClosingMaster>(reader,token);
        return data;
    }
    public static async Task<ClosingMaster?> GetClosingById(string conLocal, int id, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("PosClosing_UpdateJson", con);
        cmd.Parameters.AddWithValue("@id", id);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<ClosingMaster>(reader,token);
    }

    public static async Task<List<ClosingMaster>> GetClosingListByDate(string conLocal, ClosingReportPar par, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = new SqlCommand("PosSalesClsing_SelectByDate", con);

        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@from", par.From);
        cmd.Parameters.AddWithValue("@to", par.To);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteXmlReaderAsync(token);
        var ser = new XmlSerializer(typeof(List<ClosingMaster>), new XmlRootAttribute("ClosingList"));
        var list = ser.Deserialize(reader) as List<ClosingMaster>;
        return list;

    }
    public static async Task<List<VoucherType>> GetVoucherTypes(string conLocal, string language, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("VoucherTypes_json", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@Language", language);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<VoucherType>>(reader,token) ?? [];
    }
}
