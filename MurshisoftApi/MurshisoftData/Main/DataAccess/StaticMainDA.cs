using Microsoft.Data.SqlClient;
using MurshisoftData.Models.Main;
using MurshisoftData.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace MurshisoftData.Main.DataAccess;

public class StaticMainDA
{
    public static async Task<List<FinancialYearModel>> GetFinancialYears(string conLocal, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("FinancialYearsList", con);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        var data = await MyCommand.GetJsonSerialized<List<FinancialYearModel>>(reader, token);
        return data ?? [];
    }
    public static async Task<List<Branch>> GetBranches(string conLocal, int parentId, int divisionId, CancellationToken token = default)
    {
        var lst = new List<Branch>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Branches_SelectByParentID", con);
        cmd.Parameters.AddWithValue("@ParentID", parentId);
        cmd.Parameters.AddWithValue("@DivisionId", divisionId);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var branch = new Branch();
            branch.BranchId = reader.GetInt32(0);
            branch.BranchName = reader.GetString(1);
            branch.ParentId = reader.GetValue(2) == DBNull.Value ? 0 : reader.GetInt32(2);
            branch.Level = reader.GetInt32(3);
            lst.Add(branch);
        }
        return lst;
    }
    public static async Task<DateTime> GetLastStockTakeDate(string conLocal, int financialYear, int branchId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("LastStockTakeDateByBranch", con);
        cmd.Parameters.AddWithValue("@FinancialYear", financialYear);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        await con.OpenAsync(token);
        var date = await cmd.ExecuteScalarAsync(token);
        if (date == null || date == DBNull.Value) return DateTime.Today;
        return (DateTime)date;
    }
    public static async Task<List<Menu>> GetMenu(string conLocal, int groupId, string language, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Menu_Select", con);
        cmd.Parameters.AddWithValue("@GroupId", groupId);
        cmd.Parameters.AddWithValue("@Language", language);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        var data = await MyCommand.GetJsonSerialized<List<Menu>>(reader, token);
        return data ?? [];
    }
    public static async Task<string> GetNews(string conLocal, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("GetNews", con);
        await con.OpenAsync(token);
        var data = await cmd.ExecuteScalarAsync(token);
        return data?.ToString() ?? string.Empty;
    }
    public static async Task<List<MenuToolBar>> GetToolBar(string conLocal, int groupId, string language, CancellationToken token = default)
    {
        List<MenuToolBar> list = new List<MenuToolBar>();
        using SqlConnection con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("MenuToolBar_Select", con);
        cmd.Parameters.AddWithValue("@GroupID", groupId);
        cmd.Parameters.AddWithValue("@Language", language);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            MenuToolBar bar = new MenuToolBar();
            bar.MenuID = reader.GetString(0);
            bar.MenuName = reader.GetString(1);
            bar.ToolBarImage = (byte[])reader[2];
            bar.ToolBarHotImage = (byte[])reader[3];
            list.Add(bar);
        }
        return list;
    }
    public static async Task<List<TaxType>> GetTaxTypes(string conLocal, CancellationToken token = default)
    {
        var lst = new List<TaxType>(); ;
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("TaxTypes_Select", con);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            lst.Add(new TaxType
            {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                TaxPercent = reader.GetDecimal(2),
                CategoryCode = reader.GetString(3),
                ReasonCode = reader.GetString(4),
                Description = reader.GetString(5)
            });
        }
        return lst;
    }
    public static async Task<StockItemView?> GetStockViewPerLine(string conLoal, StockViewPar par, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLoal);
        using var cmd = MyCommand.CmdProc("StockView_SelectByIDJson", con);
        cmd.Parameters.AddWithValue("@ItemID", par.id);
        cmd.Parameters.AddWithValue("@FinancialYear", par.financialyear);
        cmd.Parameters.AddWithValue("@BranchID", par.branchId);
        if (!string.IsNullOrEmpty(par.customerId))
            cmd.Parameters.AddWithValue("@CustomerID", par.customerId);
        cmd.Parameters.AddWithValue("@TransactionID", par.transactionId);
        if (par.unitId > 0)
            cmd.Parameters.AddWithValue("@UnitID", par.unitId);
        await cmd.Connection.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        var data = await MyCommand.GetJsonSerialized<StockItemView>(reader, token);
        if (data == null) return null;
        data.RackID = "";
        data.ExpiryDate = new DateTime(1900, 1, 1);
        if (data.Units == null)
        {
            data.Units = [new() { UnitID = 0, UnitName = data?.UnitName?.Length > 0 ? data.UnitName : par.DefaultUnitName }];
        }
        return data;
    }
    public static async Task<List<Unit>> GetUnitsByItemId(string conLocal, string itemId, CancellationToken token = default)
    {
        using SqlConnection con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Units_ByItemID_json", con);
        cmd.Parameters.AddWithValue("@ItemID", itemId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<Unit>>(reader, token) ?? [];
    }
    public static async Task<int> ItemTransactionsCount(string conLocal, string itemId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("ItemTransactionsCount", con);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        await con.OpenAsync(token);
        var data = await cmd.ExecuteScalarAsync(token);
        return data == null || data == DBNull.Value ? 0 : (int)data;
    }
    public static async Task ItemDelete(string conLocal, string itemId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Stock_DELETE", con);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
    }
    public static async Task<DataTableJson> ItemsList(string conLocal, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("ItemsList", con);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> ItemsByCategory(string conLocal, string categoryId, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("ItemsByCategory", con);
        cmd.Parameters.AddWithValue("@CategoryId", categoryId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> CategoriesDT(string conLocal, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("StockCategories", con);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<int> CategoryItemsCount(string conLocal, string categoryId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("CategoryItemsCount", con);
        cmd.Parameters.AddWithValue("@CategoryId", categoryId);
        await con.OpenAsync(token);
        var data = await cmd.ExecuteScalarAsync(token);
        return data == null || data == DBNull.Value ? 0 : (int)data;
    }
    public static async Task<int> UnitIdTransactionCount(string conLocal, int unitId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("UnitIDTransactionCount", con);
        cmd.Parameters.AddWithValue("@UnitID", unitId);
        await con.OpenAsync(token);
        var data = await cmd.ExecuteScalarAsync(token);
        return data == null || data == DBNull.Value ? 0 : (int)data;
    }
    public static async Task<DataTableJson> PaymentDueDateTotal(string conLocal, int voucherTypeId, DateTime from, DateTime to, int financialYear, int branchId, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("PaymentDueDateTotal", con);
        cmd.Parameters.AddWithValue("@Date1", from);
        cmd.Parameters.AddWithValue("@Date2", to);
        cmd.Parameters.AddWithValue("@VoucherTypeID", voucherTypeId);
        cmd.Parameters.AddWithValue("@FinancialYear", financialYear);
        cmd.Parameters.AddWithValue("@BranchID", branchId);

        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> PaymentDueDate(string conLocal, int voucherTypeId, DateTime from, DateTime to, int reportType, int branchId, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("PaymentDueDates", con);
        cmd.Parameters.AddWithValue("@Date1", from);
        cmd.Parameters.AddWithValue("@Date2", to);
        cmd.Parameters.AddWithValue("@VoucherTypeID", voucherTypeId);
        cmd.Parameters.AddWithValue("@ReportType", reportType);
        cmd.Parameters.AddWithValue("@BranchID", branchId);

        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> ItemsBySupplier(string conLocal, string customerId, DateTime from, DateTime to, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("ItemsBySupplier", con);
        cmd.Parameters.AddWithValue("@Date1", from);
        cmd.Parameters.AddWithValue("@Date2", to);
        cmd.Parameters.AddWithValue("@CustomerID", customerId);

        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> SuppliersByItem(string conLocal, string itemId, DateTime from, DateTime to, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("SuppliersByItem", con);
        cmd.Parameters.AddWithValue("@Date1", from);
        cmd.Parameters.AddWithValue("@Date2", to);
        cmd.Parameters.AddWithValue("@ItemID", itemId);

        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> StockMostMoving(string conLocal, int BranchID, int VoucherTypeID, int ReportType, int nRecords, DateTime from, DateTime to, string refNo, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("StockMostMoving", con);
        cmd.Parameters.AddWithValue("@Date1", from);
        cmd.Parameters.AddWithValue("@Date2", to);
        cmd.Parameters.AddWithValue("@BranchID", BranchID);
        cmd.Parameters.AddWithValue("@Type", ReportType);
        cmd.Parameters.AddWithValue("@Quantity", nRecords);
        cmd.Parameters.AddWithValue("@VoucherTypeID", VoucherTypeID);
        cmd.Parameters.AddWithValue("@RefNo", refNo);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> StockMostProfit(string conLocal, int BranchID, int ReportType, int nRecords, DateTime from, DateTime to, string refNo, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("StockMostProfit", con);
        cmd.Parameters.AddWithValue("@Date1", from);
        cmd.Parameters.AddWithValue("@Date2", to);
        cmd.Parameters.AddWithValue("@BranchID", BranchID);
        cmd.Parameters.AddWithValue("@Quantity", nRecords);
        cmd.Parameters.AddWithValue("@type", ReportType);
        cmd.Parameters.AddWithValue("@RefNo", refNo);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> StockTakeDates(string conLocal, int BranchID, int financialYear, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("StockTakeDatesSelect", con);
        cmd.Parameters.AddWithValue("@BranchID", BranchID);
        cmd.Parameters.AddWithValue("@FinancialYear", financialYear);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<CustomerInfo?> GetCustomerInfo(string conLocal, int financialYear, int branchId, string customerId, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("CustomerInfoJson", con);
        cmd.Parameters.AddWithValue("@FinancialYear", financialYear);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@CustomerID", customerId);

        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<CustomerInfo>(reader);

    }
    public static async Task<DataTableJson> RestaurantProductsList(string conLocal, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("RestaurantProducts_List", con);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> RawMaterialsByProductId(string conLocal, string productId, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("ProductRawMaterials_SelectByProductId", con);
        cmd.Parameters.AddWithValue("@ProductID", productId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<Invoice?> InvoiceByTransactionId(string conLocal, string transactionId, string userName, string language, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Invoice_Xml", con);
        await con.OpenAsync(token);
        cmd.Parameters.AddWithValue("@TransactionID", transactionId);
        cmd.Parameters.AddWithValue("@PrintedBy", userName);
        cmd.Parameters.AddWithValue("Language", language);
        using XmlReader reader = await cmd.ExecuteXmlReaderAsync(token);
        var serialize = new XmlSerializer(typeof(Invoice), new XmlRootAttribute("Invoice"));
        return serialize.Deserialize(reader) as Invoice;
    }
    public static async Task DeleteTransaction(string conLoc, string transactionId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLoc);
        using var cmd = MyCommand.CmdProc("StockTransaction_Delete", con);
        cmd.Parameters.AddWithValue("@TransactionID", transactionId);
        await con.OpenAsync(token);
        await cmd.ExecuteScalarAsync(token);
    }
    public static async Task<bool> UserHasRight(string conLocal, int rightType, int userId, int groupId, string menuId, int rightId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("UserHasRight", con);
        cmd.Parameters.AddWithValue("@RightType", rightType);
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@GroupID", groupId);
        cmd.Parameters.AddWithValue("@MenuID", menuId);
        cmd.Parameters.AddWithValue("@RightId", rightId);
        await con.OpenAsync(token);
        var data = await cmd.ExecuteScalarAsync(token);
        return data == null || data == DBNull.Value ? false : (bool)data;
    }
    public static async Task<DataTableJson> CashAccountsList(string conLocal, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("CashAccountsList", con);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> BankAccountsList(string conLocal, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("BankAccountsList", con);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> GetAllBranches(string conLocal, string language, int all, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("Branches_All_List", con);
        cmd.Parameters.AddWithValue("@language", language);
        cmd.Parameters.AddWithValue("@all", all);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<List<VoucherNavation>> GetVoucherNavigations(string conLocal, int year, int branchId, int voucherTypeId, CancellationToken token = default)
    {
        var lst = new List<VoucherNavation>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("VoucherNavigations", con);

        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@FinancialYear", year);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@VoucherTypeID", voucherTypeId);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);

        while (await reader.ReadAsync(token))
        {
            lst.Add(new VoucherNavation
            {
                VoucherNo = reader.GetString(0),
                VoucherNoInt = reader.GetInt32(1),
                SerialNo = reader.GetInt32(2),
                RecordsCount = reader.GetInt32(3),
                RowNumber = reader.GetInt64(4)
            });
        }
        return lst;
    }

    public static async Task<VoucherNavation?> GetVoucherNavigationByVoucherNo(string conString, string voucherNo, CancellationToken token = default)
    {
        using var con = new SqlConnection(conString);
        using var cmd = MyCommand.CmdProc("VoucherNavigationByVoucherNo", con);
        cmd.Parameters.AddWithValue("@VoucherNo", voucherNo);
        await con.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        if (await reader.ReadAsync(token))
        {
            return new VoucherNavation
            {
                VoucherNo = reader.GetString(0),
                VoucherNoInt = reader.GetInt32(1),
                SerialNo = reader.GetInt32(2),
                RecordsCount = reader.GetInt32(3),
                RowNumber = reader.GetInt64(4)
            };
        }
        return null;
    }
    public static async Task<VoucherMain?> SaveVoucher(string conLocal, VoucherMain voucher, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Voucher_UpdateJson", con);
        var json = JsonSerializer.Serialize(voucher, MyCommand.option);
        cmd.Parameters.AddWithValue("@json", json);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<VoucherMain>(reader, token);
    }
    public static async Task<VoucherMain?> GetVoucherById(string conLocal, string voucherNo, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Voucher_SelectByNo_Json", con);
        cmd.Parameters.AddWithValue("@voucherNo", voucherNo);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<VoucherMain>(reader, token);
    }
    public static async Task<List<VoucherMain>> VouchersByDateRange(string conLocal, int branchId, int voucherTypeId, DateTime from, DateTime to, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Voucher_SelectByDateRange_Json", con);
        cmd.Parameters.AddWithValue("@VoucherTypeID", voucherTypeId);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@from", from);
        cmd.Parameters.AddWithValue("@to", to);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<VoucherMain>>(reader, token) ?? [];
    }

    public static async Task<DataTableJson> VoucherSearchDT(string conLocal, VoucherSearchPar par, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("VoucherSearch", con);
        cmd.Parameters.AddWithValue("@VoucherTypeID", par.VoucherTypeId);
        cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
        cmd.Parameters.AddWithValue("@FinancialYear", par.FinancialYear);
        cmd.Parameters.AddWithValue("@BillTo", par.BillTo);
        cmd.Parameters.AddWithValue("@Details", par.Details);
        cmd.Parameters.AddWithValue("@Details2", par.Details2);
        cmd.Parameters.AddWithValue("@ChequeNo", par.ChequeNo);
        cmd.Parameters.AddWithValue("@OtherNo", par.OtherNo);
        cmd.Parameters.AddWithValue("@DebitAmount", par.Debit);
        cmd.Parameters.AddWithValue("@CreditAmount", par.Credit);
        cmd.Parameters.AddWithValue("@CostCenterID", par.CostCenterId);

        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> VoucherDetailByVoucherNoDt(string conLocal, string voucherNo, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("VoucherDetailByVoucherNo", con);
        cmd.Parameters.AddWithValue("@VoucherNo", voucherNo);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<string> GetNextVoucherNo(string conLocal, int financialYear, int branchId, int voucherTypeId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("NextVoucherNo", con);
        cmd.Parameters.AddWithValue("@FinancialYear", financialYear);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@VoucherTypeId", voucherTypeId);
        cmd.Parameters.Add("@VoucherNo", SqlDbType.NVarChar, 50).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
        var id = cmd.Parameters["@VoucherNo"].Value;
        if (id == null || id == DBNull.Value) return "";
        return id.ToString();
    }
    public static async Task<FooterInfo?> GetItemFooterInfo(string conLocal, string itemId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Items_FooterInfo", con);
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        await cmd.Connection.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        if (await reader.ReadAsync(token))
        {
            return new FooterInfo
            {
                ItemId = reader.GetString(0),
                Balance = reader.GetDecimal(1),
                PiecesOnHand = reader.GetDecimal(2),
                LastPurchasePrice = reader.GetDecimal(3),
                LastSalesPrice = reader.GetDecimal(4)
            };
        }
        return null;
    }
    public static async Task<List<ContractItem>> GetItemsByContractID(string conLocal, string contractId, CancellationToken token = default)
    {
        List<ContractItem> items = new List<ContractItem>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Items_ByContractID", con);
        cmd.Parameters.AddWithValue("@ContractID", contractId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync())
        {
            ContractItem item = new ContractItem();
            item.ItemID = reader.GetString(0);
            item.ItemName = reader.GetString(1);
            item.Quantity = reader.GetDecimal(2);
            item.CostPrice = reader.GetDecimal(3);
            item.SalesPrice = reader.GetDecimal(4);
            item.TaxPercent = reader.GetDecimal(5);

            items.Add(item);
        }
        return items;
    }
    public static async Task<decimal> GetFixedPercentage(string conLocal, string customerId, string itemId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("SalesPricePercentage_ByItemAndCustomer", con);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@CustomerID", customerId);
        cmd.Parameters.AddWithValue("@ItemID", itemId);
        await con.OpenAsync(token);
        object percent = await cmd.ExecuteScalarAsync(token);
        if (percent != null)
            return Convert.ToDecimal(percent);
        return 0;
    }
    public static async Task<string> GetNextTransactionId(string conLocal, int year, int branchId, int voucherTypeId, CancellationToken token = default)
    {
        using SqlConnection con = new SqlConnection(conLocal);
        using SqlCommand cmd = MyCommand.CmdProc("NextTransactionID", con);
        cmd.Parameters.AddWithValue("@FinancialYear", year);
        cmd.Parameters.AddWithValue("@VoucherTypeID", voucherTypeId);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.Add("@TransactionID", SqlDbType.NVarChar, 50).Direction = ParameterDirection.Output;
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
        var result = cmd.Parameters["@TransactionID"].Value;
        if (result == null) return "";
        return result.ToString();
    }
    public static async Task<DateTime> GetServerDate(string conLocal, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdText("select GETDATE()", con);
        await cmd.Connection.OpenAsync(token);
        var now = await cmd.ExecuteScalarAsync(token);
        if (now != null && now != DBNull.Value)
        {
            return (DateTime)now;
        }
        else return DateTime.Now;

    }
    public static async Task<List<TransactionNavigation>> GetTransactionNavigations(string conLocal, int year, int voucherTypeId, int branchId, CancellationToken token = default)
    {
        var lst = new List<TransactionNavigation>();
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("StockTransactionNavigations", con);
        cmd.Parameters.AddWithValue("@FinancialYear", year);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@VoucherTypeID", voucherTypeId);
        await con.OpenAsync(token);
        using (var reader = await cmd.ExecuteReaderAsync(token))
            while (await reader.ReadAsync())
            {
                var t = new TransactionNavigation();
                t.TransactionID = reader.GetString(0);
                t.TransactionIDInt = reader.GetInt32(1);
                t.SerialNo = reader.GetInt32(2);
                t.RecordsCount = reader.GetInt32(3);
                t.RowNumber = reader.GetInt64(4);

                lst.Add(t);
            }
        return lst;
    }
    public static async Task<string> CheckForPreviousReturn(string conLocal, string transactionId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("TransactionReturnCheck", con);
        cmd.Parameters.AddWithValue("@TransactionID", transactionId);
        await con.OpenAsync(token);
        var id = await cmd.ExecuteScalarAsync(token);
        return (id == null || id == DBNull.Value) ? string.Empty : id.ToString();
    }
    public static async Task<DataTableJson> CustomersSuppliersList(string conLocal, bool withEmptyRow, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("Customer_Suppliers_List", con);
        cmd.Parameters.AddWithValue("@EmptyRow", withEmptyRow);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<DataTableJson> VoucherTypesDT(string conLocal, int selectType, string language, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("VoucherTypes_Select", con);
        cmd.Parameters.AddWithValue("@Language", language);
        cmd.Parameters.AddWithValue("@selecttype", selectType);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task CreateSqlUser(string conLocal, string dbName, string userName, string password, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("UserSqlCreate", con);
        cmd.Parameters.AddWithValue("@UserName", userName);
        cmd.Parameters.AddWithValue("@Password", password);
        cmd.Parameters.AddWithValue("@DBName", dbName);
        await con.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
    }
    public static async Task<DataTableJson> TransSearch(string conLocal, int year, int branchId, int voucherTypeId, DateTime from, DateTime to, bool isTemp, string searchTerm, int searchMethod, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("TransSearch", con);
        cmd.Parameters.AddWithValue("@FinancialYear", year);
        cmd.Parameters.AddWithValue("@BranchID", branchId);
        cmd.Parameters.AddWithValue("@VoucherTypeID", voucherTypeId);

        if (from != DateTime.MinValue)
            cmd.Parameters.AddWithValue("@From", from);

        if (to != DateTime.MinValue)
            cmd.Parameters.AddWithValue("@To", to);
        cmd.Parameters.AddWithValue("@IsTemp", isTemp);

        if (!string.IsNullOrWhiteSpace(searchTerm))
            cmd.Parameters.AddWithValue("@SearchWord", searchTerm);

        cmd.Parameters.AddWithValue("@SearchMethod", searchMethod);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }

    //Zatca:
    public static async Task<string> ValidateZatcaCustomer(string conLocal, string customerId, string vatNo, string language, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("zatca.ValidateCustomer", con);
        cmd.Parameters.AddWithValue("@CustomerId", customerId);
        cmd.Parameters.AddWithValue("@VatNo", vatNo);
        cmd.Parameters.AddWithValue("@Language", language);
        cmd.Parameters.Add("@message", SqlDbType.NVarChar, 300).Direction = ParameterDirection.Output;
        await cmd.Connection.OpenAsync(token);
        await cmd.ExecuteNonQueryAsync(token);
        var msg = cmd.Parameters["@message"].Value.ToString();
        return msg;
    }
    //Workshop:
    public static async Task<List<ContractCostingMain>> GetCostingByTransactionId(string conLocal, string transactionId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("WorkCosting_Select", con);
        cmd.Parameters.AddWithValue("@transactionId", transactionId);
        await cmd.Connection.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<ContractCostingMain>>(reader, token) ?? [];
    }

    public static async Task<bool> IsPeriodClosed(string conLocal, int branchId, DateTime date, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("IsPeriodClosed", con);
        cmd.Parameters.AddWithValue("@BranchId", branchId);
        cmd.Parameters.AddWithValue("@date", date);
        await con.OpenAsync(token);
        var data = await cmd.ExecuteScalarAsync(token);
        return data == null || data == DBNull.Value ? false : (int)data > 0;
    }
    public static async Task<DataTableJson> GetPaymentMethods(string conLocal, string lang, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("PaymentMethods_Select", con);
        cmd.Parameters.AddWithValue("@Language", lang);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<List<ParentAccount>> GetParentChildAccounts(string conLocal, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Accounts_ParentChildListJson", con);
        await cmd.Connection.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<ParentAccount>>(reader, token) ?? [];
    }
    public static async Task<bool> IsValidAccountNo(string conLocal, string accountNo, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("IsAccountNoValidSubAccount", con);
        cmd.Parameters.AddWithValue("@AccountNo", accountNo);
        await con.OpenAsync(token);
        object oval = await cmd.ExecuteScalarAsync();
        if (null != oval || oval != DBNull.Value)
            return (int)oval! > 0;

        return false;
    }
    public static async Task<DataTableJson> AccountsListWithHierarchy(string conLocal, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("AccountsListWithHierarchy", con);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    
    public static async Task<StatementMaster> AccountStatement(string conLocal, AcStatementPar par, CancellationToken token = default)
    {
        using var connection = new SqlConnection(conLocal);
        await connection.OpenAsync(token);
        using var cmd = MyCommand.CmdProc("AccountStatement_New", connection);
        cmd.Parameters.AddWithValue("@Language", par.Language);
        cmd.Parameters.AddWithValue("@VTypeID", par.VoucherTypeId);
        cmd.Parameters.AddWithValue("@BranchID", par.BranchId);
        cmd.Parameters.AddWithValue("@fyear", par.FinancialYear);
        cmd.Parameters.AddWithValue("@AccountNo", par.AccountNo);
        cmd.Parameters.AddWithValue("@Date1", par.FromDate);
        cmd.Parameters.AddWithValue("@Date2", par.ToDate);
        var master = new StatementMaster();
        master.StatementDetails = [];

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var h = new StatementDetail();
            master.AcountName = reader.GetString(0);
            h.VoucherDate = par.FromDate;
            h.Details2 = reader.GetString(1);
            h.NetBalance = reader.GetDecimal(2);
            master.TotalRows = reader.GetInt32(3);
            master.AccountTypeId = MyHelpers.TryGetInt(reader, 4);
            master.VatNumber = MyHelpers.TryGetString(reader, 5);
            master.CreditPeriod = MyHelpers.TryGetInt(reader, 6);
            master.OpeningBalance = h.NetBalance;
            decimal runningBalance = h.NetBalance;
            master.StatementDetails.Add(h);
            await reader.NextResultAsync();
            while (await reader.ReadAsync())
            {
                var d = new StatementDetail
                {
                    AccountNo = reader.GetString(0),
                    VoucherTypeName = reader.GetString(1),
                    BranchName = reader.GetString(2),
                    AccountName = reader.GetString(3),
                    VoucherDate = reader.GetDateTime(4),
                    VoucherTypeID = reader.GetInt32(5),
                    VoucherNo = reader.GetString(6),
                    Details2 = reader.GetString(7),
                    ChequeNo = reader.GetString(8),
                    OtherNo = reader.GetString(9),
                    CreditAmount = reader.GetDecimal(10),
                    DebitAmount = reader.GetDecimal(11),
                    HasDocs = MyHelpers.TryGet(reader, 12),
                    PaymentStatus = MyHelpers.TryGetInt(reader, 13),
                    InvoiceAge = MyHelpers.TryGetInt(reader, 14),
                    AmountPaid = MyHelpers.TryGetDecimal(reader, 15),
                };
                d.VoucherDate2 = MyHelpers.ConvertToHijri2(d.VoucherDate);
                runningBalance += (d.DebitAmount - d.CreditAmount);
                d.NetBalance = runningBalance;
                master.StatementDetails.Add(d);
            }
        }
        return master;
    }
    public static async Task<DataTableJson> GetList(string conLocal, int typeId, int userId, CancellationToken token = default)
    {
        var con = new SqlConnection(conLocal);
        var cmd = MyCommand.CmdProc("ListItem_Select", con);
        cmd.Parameters.AddWithValue("@typeId", typeId);
        cmd.Parameters.AddWithValue("@UserId", userId);
        await con.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetTableJson(reader);
    }
    public static async Task<List<ItemCardCategory>> GetItemCardCategories(string conLocal,int typeId, CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("ItemCardCategoriesJson", con);
        cmd.Parameters.AddWithValue("@typeId", typeId);
        await cmd.Connection.OpenAsync(token);
        var reader = await cmd.ExecuteReaderAsync(token);
        return await MyCommand.GetJsonSerialized<List<ItemCardCategory>>(reader, token) ?? [];
    }
    //async IAsyncEnumerable<T?>
    public static async IAsyncEnumerable<ItemCard?> GetItemsByCategory(string conLocal, string categoryId, [EnumeratorCancellation]CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("ItemCardItemsByCategory", con);
        cmd.Parameters.AddWithValue("@categoryId", categoryId);
        await cmd.Connection.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var card = new ItemCard();
                card.ItemID = reader.GetString(0);
            card.RefNo = reader.GetString(1);
            card.PartNo = reader.GetString(2);
            card.IsProduct = reader.GetBoolean(3);
            card.ItemName = reader.GetString(4);
            card.ItemNameEnglish = reader.GetString(5);
            card.Location = reader.GetString(6);
            card.CostPrice = reader.GetDecimal(7);
            card.SalesPrice = reader.GetDecimal(8);
            card.LastPrice = reader.GetDecimal(9);
            card.OrderLevel = reader.GetDecimal(10);
            card.Discontinued = reader.GetBoolean(11);
            card.UserName = reader.GetString(12);
            card.EntryDate = reader.GetDateTime(13);
            card.ItemSerial = reader.GetInt32(14);
            card.IsMain = reader.GetBoolean(15);
            card.AutoNum = reader.GetBoolean(16);
            card.ProfitPercent = reader.GetDecimal(17);
            card.LeastProfitPercent= reader.GetDecimal(18);
            card.NumberOfUnits = reader.GetInt32(19);
            card.BiggestUnitCount = reader.GetDecimal(20);
            card.DiscountAmount = reader.GetDecimal(21);
            card.WholesalePrice = reader.GetDecimal(22);
            card.IsService = reader.GetBoolean(23);
            card.IsMahata = reader.GetBoolean(24);
            card.ManufacturerID = reader.GetInt32(25);
            card.StockistID = reader.GetInt32(26);
            card.OrderLevelMaximum = reader.GetDecimal(27);
            card.Barcode = reader.GetString(28);
            card.HasDetails = reader.GetBoolean(29);
            card.OrderSerial = reader.GetInt32(30);
            card.AssembledProduct = reader.GetInt32(31);
            card.AccountNo = reader.GetString(32);
            card.Length = reader.GetDecimal(33);
            card.Width = reader.GetDecimal(34);
            card.Height = reader.GetDecimal(35);
            yield return card;


        }
    }
    public static async IAsyncEnumerable<ParentAccount?> GetParentAccounts(string conLocal, [EnumeratorCancellation] CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Accounts_ParentsList", con);
        await cmd.Connection.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var ac = new ParentAccount();
            ac.AccountNo = reader.GetString(0);
            ac.AccountName = reader.GetString(1);
            yield return ac;
        }
    }
    public static async IAsyncEnumerable<ChildAccount?> GetChildAccounts(string conLocal, [EnumeratorCancellation] CancellationToken token = default)
    {
        using var con = new SqlConnection(conLocal);
        using var cmd = MyCommand.CmdProc("Accounts_ChildList", con);
        await cmd.Connection.OpenAsync(token);
        using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var ac = new ChildAccount();
            ac.AccountNo = reader.GetString(0);
            ac.AccountName = reader.GetString(1);
            ac.MobileNo = reader.GetString(2);
            ac.FaxNo = reader.GetString(3);
            ac.Address = reader.GetString(4);
            ac.RefNo = reader.GetString(5);
            yield return ac;
        }
    }
}