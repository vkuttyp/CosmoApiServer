using MurshisoftData.Caching;
using MurshisoftData.Models.Main;
using MurshisoftData.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MurshiSoftData.Helpers;

namespace MurshisoftData.Main.DataAccess;

public class RestDataMain(string conLocal, bool useApi, string url)
{
   private readonly HttpClient httpClient = MyHttpClient.GetHttpClient(url);
    public async Task<List<FinancialYearModel>> GetFinancialYears()
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetFinancialYears");
            return await MyCommand.ResponseToData<List<FinancialYearModel>>(response) ?? [];
        }
        return await StaticMainDA.GetFinancialYears(conLocal);
    }
   
    public async Task<List<Branch>> GetBranches(int parentId, int divisionId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetBranches/{parentId}/{divisionId}", token);
            return await MyCommand.ResponseToData<List<Branch>>(response) ?? [];
        }
        return await StaticMainDA.GetBranches(conLocal, parentId, divisionId, token);
    }
   
    public async Task<DateTime> GetLastStockTakeDate(int financialYear, int branchId, CancellationToken token = default)
    {
        if(useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetLastStockTakeDate/{financialYear}/{branchId}", token);
            return await MyCommand.ResponseToDate(response);
        }
        return await StaticMainDA.GetLastStockTakeDate(conLocal, financialYear, branchId, token);
    }
   
    public async Task<List<Menu>> GetMenu(int groupId, string language, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetMenu/{groupId}/{language}", token);
            return await MyCommand.ResponseToData<List<Menu>>(response) ?? [];
        }
        return await StaticMainDA.GetMenu(conLocal, groupId, language, token);
    }
   
    public async Task<string> GetNews(CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetNews", token);
            return await MyCommand.ResponseToString(response);
        }
        return await StaticMainDA.GetNews(conLocal, token);
    }
    public async Task<List<MenuToolBar>> GetToolbar(int groupId, string language, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetToolbar/{groupId}/{language}", token);
            return await MyCommand.ResponseToData<List<MenuToolBar>>(response) ?? [];
        }
        return await StaticMainDA.GetToolBar(conLocal,groupId, language, token);
    }
    static List<TaxType> _taxTypes;
    public async Task<List<TaxType>> GetTaxTypes(CancellationToken token = default)
    {
        if (_taxTypes == null)
        {
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Main/GetTaxTypes", token);
                _taxTypes = await MyCommand.ResponseToData<List<TaxType>>(response) ?? [];
            }
            else
            {
                _taxTypes = await StaticMainDA.GetTaxTypes(conLocal, token);
            }
        }
        return _taxTypes;
    }

    static LRUCache<string, StockItemView> itemViewCache = new LRUCache<string, StockItemView>(10, 9);
    static PDictionary<string, List<Unit>> itemsDict = new PDictionary<string, List<Unit>>("units.json");
    public async Task<StockItemView?> GetStockViewPerLine(StockViewPar par, bool useCache = true, CancellationToken token = default)
    {
        if (!useCache || !itemViewCache.TryGet(par.Key, out var item))
        {
            StockItemView? data;
            if (useApi)
            {
                var json = JsonSerializer.Serialize(par);
                var val = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("Main/GetStockViewPerLine", val);
                data= await MyCommand.ResponseToData<StockItemView>(response);
            }
            else data = await StaticMainDA.GetStockViewPerLine(conLocal, par, token);

            if (data == null) return null;
            itemViewCache.AddReplace(par.Key, data, DateTime.UtcNow.AddMinutes(10));
            if (!itemsDict.ContainsKey(par.id))
                itemsDict.Add(par.id, data.Units.ToList());
            return data;
        }
        return itemViewCache.Get(par.Key);
    }
    public async Task<List<Unit>> GetUnitsByItemId(string itemId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetUnitsByItemId/{itemId}", token);
            return await MyCommand.ResponseToData<List<Unit>>(response) ?? [];
        }
        return await StaticMainDA.GetUnitsByItemId(conLocal, itemId, token);
    }
    public async Task<List<Unit>> GetUnitsByItemIdFromCache(string itemId, CancellationToken token = default)
    {
        if (!itemsDict.ContainsKey(itemId))
        {
            List<Unit>? data = null;
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Main/GetUnitsByItemId/{itemId}", token);
                data = await MyCommand.ResponseToData<List<Unit>>(response) ?? [];
            }
            else
            {
                data = await StaticMainDA.GetUnitsByItemId(conLocal, itemId, token);
            }
            itemsDict.Add(itemId, data);
            return data;
        }
        itemsDict.TryGetValue(itemId, out var units);
        return units;
    }
    public async Task<int> ItemTransactionsCount(string itemId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/ItemTransactionsCount/{itemId}", token);
            return await MyCommand.ResponseToInt(response);
        }
        return await StaticMainDA.ItemTransactionsCount(conLocal, itemId, token);
    }
   
    public async Task ItemDelete(string itemId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/ItemDelete/{itemId}", token);
            await MyCommand.ValidateResponse(response);
        }
        await StaticMainDA.ItemDelete(conLocal, itemId, token);
    }
   
    public async Task<DataTable?> ItemsList(CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync("Main/ItemsList", token);
            data= await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.ItemsList(conLocal,token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> ItemsByCategory(string categoryId, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/ItemsByCategory/{categoryId}", token);
            data= await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.ItemsByCategory(conLocal,categoryId, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> CategoriesDT(CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync("Main/CategoriesDT", token);
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.CategoriesDT(conLocal, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<int> CategoryItemsCount(string categoryId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/CategoryItemsCount/{categoryId}", token);
            return await MyCommand.ResponseToInt(response);
        }
        return await StaticMainDA.CategoryItemsCount(conLocal, categoryId, token);
    }
    public async Task<int> UnitIdTransactionCount(int unitId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/UnitIdTransactionCount/{unitId}", token);
            return await MyCommand.ResponseToInt(response);
        }
        return await StaticMainDA.UnitIdTransactionCount(conLocal, unitId, token);
    }
    public async Task<DataTable?> PaymentDueDateTotal(int voucherTypeId, DateTime from, DateTime to, int financialYear, int branchId, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var par=new {voucherTypeId, from,  to, financialYear, branchId};
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/PaymentDueDateTotal", val);

            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.PaymentDueDateTotal(conLocal,voucherTypeId, from, to, financialYear, branchId, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> PaymentDueDate(int voucherTypeId, DateTime from, DateTime to, int reportType, int branchId, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var par = new { voucherTypeId, from, to, reportType, branchId };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/PaymentDueDate", val);

            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.PaymentDueDate(conLocal, voucherTypeId, from, to, reportType, branchId, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> ItemsBySupplier(string customerId, DateTime from, DateTime to, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var par = new { customerId, from, to };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/ItemsBySupplier", val);

            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.ItemsBySupplier(conLocal, customerId, from, to, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> SuppliersByItem(string itemId, DateTime from, DateTime to, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var par = new { itemId, from, to };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/SuppliersByItem", val);

            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.SuppliersByItem(conLocal, itemId, from, to, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> StockMostMoving(int BranchID, int VoucherTypeID, int ReportType, int nRecords, DateTime from, DateTime to, string refNo, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var par = new { BranchID, VoucherTypeID,  ReportType, nRecords, from, to, refNo };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/StockMostMoving", val);

            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.StockMostMoving(conLocal, BranchID, VoucherTypeID, ReportType, nRecords, from, to, refNo, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> StockMostProfit(int BranchID, int ReportType, int nRecords, DateTime from, DateTime to, string refNo, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var par = new { BranchID, ReportType, nRecords, from, to, refNo };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/StockMostProfit", val);

            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.StockMostProfit(conLocal, BranchID, ReportType, nRecords, from, to, refNo, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> StockTakeDates(int BranchID, int financialYear, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/StockTakeDates/{BranchID}/{financialYear}");
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.StockTakeDates(conLocal, BranchID, financialYear, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<CustomerInfo?> GetCustomerInfo(int financialYear, int branchId, string customerId, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { financialYear, branchId, customerId };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/GetCustomerInfo", val);
            return await MyCommand.ResponseToData<CustomerInfo>(response);
        }
        return await StaticMainDA.GetCustomerInfo(conLocal, financialYear, branchId, customerId, token);
    }
    public async Task<DataTable?> RestaurantProductsList(CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/RestaurantProductsList");
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.RestaurantProductsList(conLocal, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> RawMaterialsByProductId(string productId, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/RawMaterialsByProductId/{productId}");
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.RawMaterialsByProductId(conLocal,productId, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<Invoice?> InvoiceByTransactionId(string transactionId, string userName, string language, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { transactionId, userName, language };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/InvoiceByTransactionId", val, token);
            return await MyCommand.ResponseToData<Invoice>(response);
        }
        return await StaticMainDA.InvoiceByTransactionId(conLocal, transactionId, userName, language, token);
    }
    public async Task DeleteTransaction(string transactionId, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { transactionId };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/InvoiceByTransactionId", val, token);
            await MyCommand.ValidateResponse(response);
        }
         await StaticMainDA.DeleteTransaction(conLocal, transactionId, token);
    }
    public async Task<bool> UserHasRight(int rightType, int userId, int groupId, string menuId, int rightId, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new {  rightType, userId, groupId, menuId, rightId };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/UserHasRight", val, token);
            return await MyCommand.ResponseToBool(response);
        }
        return await StaticMainDA.UserHasRight(conLocal, rightType, userId, groupId, menuId, rightId, token);
    }
    static DataTable? _cashOnly;
    public async Task<DataTable?> CashAccountsList(CancellationToken token = default)
    {
        if (_cashOnly == null)
        {
            DataTableJson? data = null;
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Main/CashAccountsList");
                data = await MyCommand.ResponseToData<DataTableJson>(response);
            }
            else
            {
                data = await StaticMainDA.CashAccountsList(conLocal, token);
            }
            _cashOnly= MyCommand.GetDataTableFromJson(data?.JsonData);
        }
        return _cashOnly;
    }
    static DataTable? _bankAccounts;
    public async Task<DataTable?> BankAccountsList(CancellationToken token = default)
    {
        if (_bankAccounts == null)
        {
            DataTableJson? data = null;
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Main/BankAccountsList");
                data = await MyCommand.ResponseToData<DataTableJson>(response);
            }
            else
            {
                data = await StaticMainDA.BankAccountsList(conLocal, token);
            }
            _bankAccounts = MyCommand.GetDataTableFromJson(data?.JsonData);
        }
        return _bankAccounts;
    }

    static DataTable? _allBranches;
    public async Task<DataTable?> GetAllBranches(string language, int all, CancellationToken token = default)
    {
        if (_allBranches == null)
        {
            DataTableJson? data = null;
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Main/GetAllBranches/{language}/{all}");
                data = await MyCommand.ResponseToData<DataTableJson>(response);
            }
            else
            {
                data = await StaticMainDA.GetAllBranches(conLocal,language,all, token);
            }
            _allBranches = MyCommand.GetDataTableFromJson(data?.JsonData);
        }
        return _allBranches;
    }
    public async Task<List<VoucherNavation>> GetVoucherNavigations(int year, int branchId, int voucherTypeId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetVoucherNavigations/{year}/{branchId}/{voucherTypeId}", token);
            return await MyCommand.ResponseToData<List<VoucherNavation>>(response) ?? [];
        }
        return await StaticMainDA.GetVoucherNavigations(conLocal, year, branchId, voucherTypeId, token);
    }
    public async Task<VoucherNavation?> GetVoucherNavigationByVoucherNo(string voucherNo, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { voucherNo };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/GetVoucherNavigationByVoucherNo", val, token);
            return await MyCommand.ResponseToData<VoucherNavation>(response);
        }
        return await StaticMainDA.GetVoucherNavigationByVoucherNo(conLocal,voucherNo, token);
    }
    public async Task<VoucherMain?> SaveVoucher(VoucherMain voucher, CancellationToken token = default)
    {
        if (voucher.sno == 0) voucher.VoucherNo = "";

        if (useApi)
        {
            var json = JsonSerializer.Serialize(voucher);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/SaveVoucher", val, token);
            voucher = await MyCommand.ResponseToData<VoucherMain>(response);
        }
        else
        {
            voucher = await StaticMainDA.SaveVoucher(conLocal, voucher, token);
        }
        return WithTotal(voucher);
    }
    public async Task<VoucherMain?> GetVoucherById(string voucherNo, CancellationToken token = default)
    {
        VoucherMain? voucher;
        if (useApi)
        {
            var par = new { voucherNo };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/GetVoucherById", val, token);
            voucher = await MyCommand.ResponseToData<VoucherMain>(response);
        }
        else
        {
            voucher = await StaticMainDA.GetVoucherById(conLocal, voucherNo, token);
        }
        return WithTotal(voucher);

    }
    private VoucherMain? WithTotal(VoucherMain? voucher)
    {
        if (voucher != null)
        {
            voucher.DebitAmountTotal = voucher.VoucherDetails?.FastSum(a => a.DebitAmount) ?? 0;
            voucher.CreditAmountTotal = voucher.VoucherDetails?.FastSum(a => a.CreditAmount) ?? 0;
        }
        return voucher;
    }
    public async Task<List<VoucherMain>> VouchersByDateRange(int branchId, int voucherTypeId, DateTime from, DateTime to, CancellationToken token = default)
    {

        if (useApi)
        {
            var par = new { branchId, voucherTypeId, from, to };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/VouchersByDateRange", val, token);
            return await MyCommand.ResponseToData<List<VoucherMain>>(response) ?? [];
        }
        return await StaticMainDA.VouchersByDateRange(conLocal, branchId, voucherTypeId, from, to, token);
    }
    public async Task<DataTable?> VoucherSearchDT(VoucherSearchPar par, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/VoucherSearchDT", val, token);
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.VoucherSearchDT(conLocal,par, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<DataTable?> VoucherDetailByVoucherNoDt(string voucherNo, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var par = new { voucherNo };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/VoucherDetailByVoucherNoDt", val, token);
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.VoucherDetailByVoucherNoDt(conLocal, voucherNo, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<string> GetNextVoucherNo(int year, int branchId, int voucherTypeId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetNextVoucherNo/{year}/{branchId}/{voucherTypeId}", token);
            return await MyCommand.ResponseToString(response);
        }
        return await StaticMainDA.GetNextVoucherNo(conLocal, year,branchId, voucherTypeId, token);
    }
    public async Task<FooterInfo?> GetItemFooterInfo(string itemId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return null;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetItemFooterInfo/{itemId}", token);
            return await MyCommand.ResponseToData<FooterInfo>(response);
        }
        return await StaticMainDA.GetItemFooterInfo(conLocal, itemId, token);
    }
    public async Task<List<ContractItem>> GetItemsByContractID(string contractId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(contractId)) return [];
        if (useApi)
        {
            var par = new { contractId };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/GetItemsByContractID", val, token);
            return await MyCommand.ResponseToData<List<ContractItem>>(response) ?? [];
        }
        return await StaticMainDA.GetItemsByContractID(conLocal, contractId, token);
    }
    public async Task<decimal> GetFixedPercentage(string customerId, string itemId, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { customerId, itemId };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/GetFixedPercentage", val, token);
            return await MyCommand.ResponseToDecimal(response);
        }
        return await StaticMainDA.GetFixedPercentage(conLocal, customerId, itemId, token);
    }
    public async Task<string> GetNextTransactionId(int year, int branchId, int voucherTypeId, CancellationToken token=default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetNextTransactionId/{year}/{branchId}/{voucherTypeId}", token);
            return await MyCommand.ResponseToString(response);
        }
        return await StaticMainDA.GetNextTransactionId(conLocal, year, branchId, voucherTypeId, token);
    }
    public async Task<DateTime> GetServerDate(CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetServerDate", token);
            return await MyCommand.ResponseToDate(response);
        }
        return await StaticMainDA.GetServerDate(conLocal, token);
    }
    public async Task<List<TransactionNavigation>> GetTransactionNavigations(int year, int voucherTypeId, int branchId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetTransactionNavigations/{year}/{voucherTypeId}/{branchId}", token);
            return await MyCommand.ResponseToData<List<TransactionNavigation>>(response) ?? [];
        }
        return await StaticMainDA.GetTransactionNavigations(conLocal,year, voucherTypeId, branchId, token);
    }
    public async Task<string> CheckForPreviousReturn(string transactionId, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { transactionId };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/CheckForPreviousReturn", val, token);
            return await MyCommand.ResponseToString(response);
        }
        return await StaticMainDA.CheckForPreviousReturn(conLocal, transactionId, token);
    }
    static DataTable? _suppliersAndCustomers;
    public async Task<DataTable?> CustomersSuppliersList(bool withEmptyRow,bool refresh, CancellationToken token = default)
    {
        if (_suppliersAndCustomers == null || refresh)
        {
            DataTableJson? data = null;
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Main/CustomersSuppliersList/{withEmptyRow}", token);
                data = await MyCommand.ResponseToData<DataTableJson>(response);
            }
            else
            {
                data = await StaticMainDA.CustomersSuppliersList(conLocal, withEmptyRow, token);
            }
            _suppliersAndCustomers= MyCommand.GetDataTableFromJson(data?.JsonData);
        }
        return _suppliersAndCustomers;
    }
    public async Task<DataTable?> VoucherTypesDT(bool withText, int selectType, string language, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/VoucherTypesDT/{selectType}/{language}", token);
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
        {
            data = await StaticMainDA.VoucherTypesDT(conLocal, selectType, language, token);
        }
        var dt= MyCommand.GetDataTableFromJson(data?.JsonData);
        if (withText)
        {
            string all = language == "Arabic" ? "<<الكل>>" : "<<ALL>>";
            var row = dt.NewRow();
            row["VoucherTypeID"] = -1;
            row["VoucherTypeName"] = all;
            dt.Rows.Add(row);
            dt.AcceptChanges();
        }
        return dt;
    }
    public async Task CreateSqlUser(string conString, string dbName, string userName, string password, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { dbName, userName, password };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/CreateSqlUser", val, token);
            await MyCommand.ValidateResponse(response);
        }
        await StaticMainDA.CreateSqlUser(conString, dbName,userName, password, token);
    }
    public async Task<DataTable?> TransSearch(int year, int branchId, int voucherTypeId, DateTime from, DateTime to, bool isTemp, string searchTerm, int searchMethod, CancellationToken token = default)
    {
        if (searchMethod < 0) searchMethod = 0;
        DataTableJson? data = null;
        if (useApi)
        {
            var par = new { year, branchId, voucherTypeId, from, to, isTemp, searchTerm, searchMethod };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/TransSearch", val, token);
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.TransSearch(conLocal, year, branchId, voucherTypeId, from, to, isTemp, searchTerm, searchMethod, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    //Zatca:
    public async Task<string> ValidateZatcaCustomer(string customerId, string vatNo, string language, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { customerId, vatNo, language };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/ValidateCustomer", val, token);
            return await MyCommand.ResponseToString(response);
        }
        return await StaticMainDA.ValidateZatcaCustomer(conLocal, customerId, vatNo,language, token);
    }
    //Workshop:
    public async Task<List<ContractCostingMain>> GetCostingByTransactionId(string transactionId, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { transactionId };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/GetCostingByTransactionId", val, token);
            return await MyCommand.ResponseToData<List<ContractCostingMain>>(response) ?? [];
        }
        return await StaticMainDA.GetCostingByTransactionId(conLocal, transactionId, token);
    }
    public async Task<bool> IsPeriodClosed(int branchId, DateTime date, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { branchId, date };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/IsPeriodClosed", val, token);
            return await MyCommand.ResponseToBool(response);
        }
        return await StaticMainDA.IsPeriodClosed(conLocal,branchId, date, token);
    }
    static DataTable? _PaymentMethods;
    public async Task<DataTable?> GetPaymentMethods(string lang, CancellationToken token = default)
    {
        if (_PaymentMethods == null)
        {
            DataTableJson? data = null;
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Main/GetPaymentMethods/{lang}", token);
                data = await MyCommand.ResponseToData<DataTableJson>(response);
            }
            else
            {
                data = await StaticMainDA.GetPaymentMethods(conLocal,lang, token);
            }
            _PaymentMethods = MyCommand.GetDataTableFromJson(data?.JsonData);
        }
        return _PaymentMethods;
    }
    static List<ParentAccount>? _parentAccounts;
    public async Task<List<ParentAccount>> GetParentChildAccounts(bool refresh, CancellationToken token = default)
    {
        if (_parentAccounts == null || refresh)
        {
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Main/GetParentChildAccounts", token);
                _parentAccounts =await MyCommand.ResponseToData<List<ParentAccount>>(response) ?? [];
            }
            _parentAccounts = await StaticMainDA.GetParentChildAccounts(conLocal, token);
        }
        return _parentAccounts;
    }
    static List<ChildAccount>? _childAccounts;
    public async Task<List<ChildAccount>> GetChildAccounts(bool refresh)
    {
        var acs = await GetParentChildAccounts(refresh);
        if (acs == null) return new List<ChildAccount>();

        _childAccounts = new List<ChildAccount>();
        foreach (var item in acs)
        {
            foreach (var child in item.ChildAccounts)
            {
                _childAccounts.Add(child);
            }
        }
        return _childAccounts;
    }
    public async Task<bool> IsValidAccountNo(string accountNo, CancellationToken token = default)
    {
        if (useApi)
        {
            var par = new { accountNo };
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/IsValidAccountNo", val, token);
            return await MyCommand.ResponseToBool(response);
        }
        return await StaticMainDA.IsValidAccountNo(conLocal, accountNo, token);
    }
    public async Task<DataTable?> AccountsListWithHierarchy(CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync("Main/AccountsListWithHierarchy", token);
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.AccountsListWithHierarchy(conLocal, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<StatementMaster?> AccountStatement(AcStatementPar par, CancellationToken token = default)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Main/AccountStatement", val, token);

            return await MyCommand.ResponseToData<StatementMaster>(response);
        }
        return await StaticMainDA.AccountStatement(conLocal, par, token);
    }
    public async Task<DataTable?> GetList(int typeId, int userId, CancellationToken token = default)
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetList/{typeId}/{userId}", token);
            data = await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else
            data = await StaticMainDA.GetList(conLocal,typeId, userId, token);
        return MyCommand.GetDataTableFromJson(data?.JsonData);
    }
    public async Task<List<ItemCardCategory>> GetItemCardCategories(int typeId, CancellationToken token = default)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Main/GetItemCardCategories/{typeId}", token);
            return await MyCommand.ResponseToData<List<ItemCardCategory>>(response) ?? [];
        }
        return await StaticMainDA.GetItemCardCategories(conLocal, typeId, token);
    }
    public  IAsyncEnumerable<ItemCard?>? GetItemsByCategory(string categoryId,CancellationToken token = default)
    {
        if (useApi)
        {
            Task.Run(async () =>
            {
                var response = await httpClient.GetAsync($"Main/GetItemsByCategory/{categoryId}", HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"{error} is {response.StatusCode}");
                }
                using var stream = await response.Content.ReadAsStreamAsync();
                return JsonSerializer.DeserializeAsyncEnumerable<ItemCard?>(stream, cancellationToken: token);
            }, token);

        }
        return StaticMainDA.GetItemsByCategory(conLocal, categoryId, token);
    }
    public IAsyncEnumerable<ParentAccount?>? GetParentAccounts(CancellationToken token = default)
    {
        if (useApi)
        {
            Task.Run(async () =>
            {
                var response = await httpClient.GetAsync($"Main/GetParentAccounts", HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"{error} is {response.StatusCode}");
                }
                using var stream = await response.Content.ReadAsStreamAsync();
                return JsonSerializer.DeserializeAsyncEnumerable<ParentAccount?>(stream, cancellationToken: token);
            }, token);

        }
        return StaticMainDA.GetParentAccounts(conLocal, token);
    }
    public IAsyncEnumerable<ChildAccount?>? GetChildAccounts(CancellationToken token = default)
    {
        if (useApi)
        {
            Task.Run(async () =>
            {
                var response = await httpClient.GetAsync($"Main/GetChildAccounts", HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"{error} is {response.StatusCode}");
                }
                using var stream = await response.Content.ReadAsStreamAsync();
                return JsonSerializer.DeserializeAsyncEnumerable<ParentAccount?>(stream, cancellationToken: token);
            }, token);

        }
        return StaticMainDA.GetChildAccounts(conLocal, token);
    }
}
