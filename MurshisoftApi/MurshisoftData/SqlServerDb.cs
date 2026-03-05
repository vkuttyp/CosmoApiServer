using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MurshisoftData.DataAccess;
using MurshisoftData.Main.DataAccess;
using MurshisoftData.Models.Main;
using MurshisoftData.Models;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MurshisoftData.Main;

namespace MurshisoftData;


public class SqlServerDb(ILogger<SqlServerDb> logger, IConfiguration config)
{
    // Fix CS8601: Use null-coalescing operator to throw if connection string is missing.
    private readonly string conLocal = config.GetConnectionString("LocalDb")
        ?? throw new InvalidOperationException("Connection string 'LocalDb' not found.");

    private readonly string conRemote = config.GetConnectionString("RemoteDb")
    ?? throw new InvalidOperationException("Connection string 'RemoteDb' not found.");

    public async Task<List<ItemCard>> StockSyncGetPending(string itemId, CancellationToken token=default)
    {
        return await StaticPOSDA.StockSyncGetPending(conLocal, itemId, token);
    }
    public async Task SaveStockToRemote(ItemCard item, CancellationToken token)
    {
        await StaticPOSDA.SaveStockToRemote(conRemote, conLocal, item, token);
    }

    public async Task<List<VoucherType>> GetVoucherTypes(string language, CancellationToken token = default)
    {
        return await StaticPOSDA.GetVoucherTypes(conLocal, language,token);
    }

    public async Task<List<TransactionMain>> TransSyncGetPending(string transactionId, CancellationToken token)
    {
        return await StaticPOSDA.TransSyncGetPending(conLocal, transactionId, token);
    }
    public async Task TransSyncLocalUpdate(string transactionId, string remoteId, CancellationToken token)
    {
        await StaticPOSDA.TransSyncLocalUpdate(conLocal, transactionId, remoteId, token);
    }
    //public async Task<List<TransactionMain>> TransSyncCompletedUpdate(string transactionId)
    //{
    //   return await StaticDA.TransSyncCompletedUpdate(conLocal, transactionId);
    //}
    public async Task SaveTransactionToRemote(TransactionMain transactionMain, CancellationToken token)
    {
        await StaticPOSDA.SaveTransactionToRemote(conRemote, conLocal, transactionMain, token);
    }
    public async Task<List<SalesCustomer>> GetPosCustomers(CancellationToken token = default)
    {
        return await StaticPOSDA.GetPosCustomers(conLocal, token);
    }
    public async Task<List<PosCategory>> GetPosCategories(string userName, CancellationToken token = default)
    {
        return await StaticPOSDA.GetPosCategories(conLocal, userName,token);
    }
    public async Task<int> SavePosClosing(PosSalesClosingMaster closing, CancellationToken token = default)
    {
        return await StaticPOSDA.SavePosClosing(conLocal, closing,token);
    }
    public async Task<List<PosSalesClosingDetail>> OutstandingsByUser(string userName, CancellationToken token = default)
    {
        return await StaticPOSDA.OutstandingsByUser(conLocal, userName,token);
    }
    public async Task<DataTableJson> GetPosUsers(CancellationToken token = default)
    {
        return await StaticPOSDA.GetPosUsers(conLocal,token);
    }
    public async Task UpdateSpanResponse(SpanResponseData data, CancellationToken token = default)
    {
        await StaticPOSDA.UpdateSpanResponse(conLocal, data,token);
    }
    public async Task<DataTableJson> CustomersSalesReport(CustomerReportPar par, CancellationToken token = default)
    {
        return await StaticPOSDA.CustomersSalesReport(conLocal, par,token);
    }
    public async Task<int> UpdatePosCustomer(SalesCustomer customer, CancellationToken token = default)
    {
        return await StaticPOSDA.UpdatePosCustomer(conLocal, customer,token);
    }
    public async Task<SalesCustomer?> GetSalesCustomerByMobileNo(string mobileNo, CancellationToken token = default)
    {
        return await StaticPOSDA.GetSalesCustomerByMobileNo(conLocal, mobileNo,token);
    }
    public async Task<POSLock?> GetByCashierUserName(string cashierUserName, CancellationToken token = default)
    {
        return await StaticPOSDA.GetByCashierUserName(conLocal, cashierUserName,token);
    }
    public async Task<List<DepartmentPrinter>?> LoadPrinters(int branchId, CancellationToken token = default)
    {
        return await StaticPOSDA.LoadPrinters(conLocal, branchId,token);
    }

    public async Task<int> SavePrinter(DepartmentPrinter printer, CancellationToken token = default)
    {
        return await StaticPOSDA.SavePrinter(conLocal, printer,token);
    }
    public async Task<List<RestaurentHall>?> GetHallsAndTables(int branchId, CancellationToken token = default)
    {
        return await StaticPOSDA.GetHallsAndTables(conLocal, branchId,token);
    }
    public async Task<List<RestaurantCategory>?> GetCategoriesWithAddons(int branchId, CancellationToken token = default)
    {
        return await StaticPOSDA.GetCategoriesWithAddons(conLocal, branchId,token);
    }
    public async Task<List<RestaurantItem>?> LoadMenuItems(int branchId, int providerId, CancellationToken token = default)
    {
        return await StaticPOSDA.LoadMenuItems(conLocal,branchId, providerId,token);
    }
    public async Task<List<RestaurantItem>> GetChildListByRawMaterial(string rawMaterialId, CancellationToken token = default)
    {
        return await StaticPOSDA.GetChildListByRawMaterial(conLocal, rawMaterialId,token);
    }
    public async Task<List<SpanCommission>> GetSpanTypes( CancellationToken token = default)
    {
        return await StaticPOSDA.GetSpanTypes(conLocal,token);
    }
    public async Task<List<OnlineProvider>> GetProviders(CancellationToken token = default)
    {
        return await StaticPOSDA.GetProviders(conLocal,token);
    }
    public async Task UpdateCancelledItem(ItemDeletePar par, CancellationToken token = default)
    {
        await StaticPOSDA.UpdateCancelledItem(conLocal, par,token);
    }
    public async Task<string?> SaveCategory(ItemCard stock, CancellationToken token = default)
    {
        return await StaticPOSDA.SaveCategory(conLocal, stock, token);
    }
    public async Task<ItemCard?> SaveStock(ItemCard stock, CancellationToken token = default)
    {
        return await StaticPOSDA.SaveStock(conLocal, stock,token);
    }
    public async Task<bool> ItemExists(string barcode, CancellationToken token = default)
    {
        return await StaticPOSDA.ItemExists(conLocal, barcode,token);
    }
    public async Task<ItemCard?> GetStockById(string id, CancellationToken token = default)
    {
        return await StaticPOSDA.GetStockById(conLocal, id,token);
    }
    public async Task<InvoicePrintResult> GetInvoice(InvoicePrintParam par, CancellationToken token = default)
    {
        return await StaticPOSDA.GetInvoice(conLocal, par,token);
    }
    public async Task<RestInvoicePrintResult> RestInvoiceData(RestInvoiceParm par, CancellationToken token = default)
    {
        return await StaticPOSDA.RestInvoiceData(conLocal, par,token);
    }
    public async Task<List<DepartmentPrinter>> GetDepartmentPrinters(int branchId, CancellationToken token = default)
    {
        return await StaticPOSDA.GetDepartmentPrinters(conLocal, branchId,token);
    }
    public async Task<string> GetBase64(string transactionId, CancellationToken token = default)
    {
        return await StaticPOSDA.GetBase64(conLocal, transactionId,token);

    }
    public async Task<StockListWithLastInventory> GetStockList(int financialYear, int branchId, int voucherTypeId, int searchMethod, string language, CancellationToken token = default)
    {
        return await StaticPOSDA.GetStockList(conLocal, financialYear, branchId, voucherTypeId, searchMethod, language,token);
    }
    public async Task<StockWithUpdatedBalance> GetUpdatedBalance(int financialYear, int branchId, int lastInventoryId, CancellationToken token = default)
    {
        return await StaticPOSDA.GetUpdatedBalance(conLocal, financialYear, branchId, lastInventoryId,token);
    }

    public async Task<List<User>> UsersList(int branchId, int typeId, CancellationToken token = default)
    {
        return await StaticPOSDA.UsersList(conLocal, branchId, typeId,token);
    }
    public async Task LogOff(int loginId, CancellationToken token = default)
    {
        await StaticPOSDA.LogOff(conLocal, loginId,token);

    }
    public async Task<string> SaveTransaction(TransactionMain transactionMain, CancellationToken token = default)
    {
        return await StaticPOSDA.SaveTransaction(conLocal, transactionMain,token);
    }
    public async Task<string> SaveTransactionTemp(TransactionMain transactionMain, CancellationToken token = default)
    {
        return await StaticPOSDA.SaveTransactionTemp(conLocal, transactionMain,token);
    }
    public async Task<List<TempSearchResult>> SearchTemp(int branchId, int voucherTypeId, string userName, CancellationToken token = default)
    {
        return await StaticPOSDA.SearchTemp(conLocal, branchId, voucherTypeId, userName,token);
    }

    public async Task<TransactionMain?> SelectTransactionByID(TransactioinByIdPar par, CancellationToken token = default)
    {
        return await StaticPOSDA.SelectTransactionByID(conLocal, par,token);
    }

    public async Task UpdateClearScreen(TransactionMain transactionMain, CancellationToken token = default)
    {
        await StaticPOSDA.UpdateClearScreen(conLocal, transactionMain,token);
    }
    public async Task<SessionData?> UserLogin(LoginData login, CancellationToken token = default)
    {
        return await StaticPOSDA.UserLogin(conLocal, login,token);
    }

    public async Task<NextIdResult> GetNextIds(NextIdParam par, CancellationToken token = default)
    {
        return await StaticPOSDA.GetNextIds(conLocal, par,token);
    }

    public async Task<SalesDetail> GetSalesByLogin(int loginId, CancellationToken token = default)
    {
        return await StaticPOSDA.GetSalesByLogin(conLocal, loginId,token);
    }


    public async Task<List<PosItemDetail>> UnitDetailByBarcode(PosItemPar par, CancellationToken token = default)
    {
        return await StaticPOSDA.UnitDetailByBarcode(conLocal, par,token);
    }

    public async Task<PosItemDetail> UnitDetailSingleByBarcode(PosItemPar par, CancellationToken token = default)
    {
        return await StaticPOSDA.UnitDetailSingleByBarcode(conLocal, par,token);
    }
    public async Task<PosItemDetail> ItemDettailByBarcodePharmacy(string barcode, decimal discount, decimal discountPercent, int financialYear, int branchId, CancellationToken token = default)
    {
        return await StaticPOSDA.ItemDettailByBarcodePharmacy(conLocal, barcode, discount, discountPercent, financialYear, branchId,token);

    }
    public async Task<List<BarcodeSearchResult>> SearchByBarcode(string barcode, CancellationToken token = default)
    {
        return await StaticPOSDA.SearchByBarcode(conLocal, barcode,token);
    }

    public async Task<List<Account>?> GetCustomers(int userId, CancellationToken token = default)
    {
        return await StaticPOSDA.GetCustomers(conLocal, userId,token);
    }

    public async Task<bool> IsCreditAllowed(CreditLimitPar par, CancellationToken token = default)
    {
        return await StaticPOSDA.IsCreditAllowed(conLocal, par,token);
    }
    public async Task<List<PaymentType>> GetPaymentTypes(int branchId, string language, CancellationToken token = default)
    {
        return await StaticPOSDA.GetPaymentTypes(conLocal, branchId, language,token);

    }
    public async Task<List<InvoiceSearchResult>> SearchInvoices(InvoiceSearchParam par, CancellationToken token = default)
    {
        return await StaticPOSDA.SearchInvoices(conLocal, par,token);
    }
    public async Task<List<ItemCategory>> GetCategories(CancellationToken token = default)
    {
        return await StaticPOSDA.GetCategories(conLocal,token);
    }

    public async Task<List<ItemCategory>> GetCategoriesRestaurant(CancellationToken token = default)
    {
        return await StaticPOSDA.GetCategoriesRestaurant(conLocal,token);
    }
    public async Task<List<BranchBalance>> GetBranchBalance(int financialYear, string itemId, CancellationToken token = default)
    {
        return await StaticPOSDA.GetBranchBalance(conLocal, financialYear, itemId,token);
    }
    public async Task<(string?, string?)> GetLastIds(int branchId, string userName, CancellationToken token = default)
    {
        return await StaticPOSDA.GetLastIds(conLocal, branchId, userName,token);
    }
    public async Task<List<XReportModel>> XReport(XReportParam par, CancellationToken token = default)
    {
        return await StaticPOSDA.XReport(conLocal, par,token);
    }

    public async Task<List<XReportByCategoryModel>> XReportByCategory(XReportByCategoryParam par, CancellationToken token = default)
    {
        return await StaticPOSDA.XReportByCategory(conLocal, par,token);
    }
    public async Task<ClosingMaster> CloseSales(CloseSalesParam par, CancellationToken token = default)
    {
        return await StaticPOSDA.CloseSales(conLocal, par,token);
    }
    public async Task<ClosingMaster?> GetClosingById(int id, CancellationToken token = default)
    {
        return await StaticPOSDA.GetClosingById(conLocal, id,token);
    }

    public async Task<List<ClosingMaster>> GetClosingListByDate(ClosingReportPar par, CancellationToken token = default)
    {
        return await StaticPOSDA.GetClosingListByDate(conLocal, par,token);

    }
    //main
    public async Task<List<FinancialYearModel>> GetFinancialYears(CancellationToken token = default)
    {
        return await StaticMainDA.GetFinancialYears(conLocal, token);
    }
    public async Task<List<Branch>> GetBranches(int parentId, int divisionId, CancellationToken token = default)
    {
        return await StaticMainDA.GetBranches(conLocal, parentId, divisionId, token);
    }
    public async Task<DateTime> GetLastStockTakeDate(int financialYear, int branchId, CancellationToken token = default)
    {
        return await StaticMainDA.GetLastStockTakeDate(conLocal, financialYear, branchId, token);
    }
    public async Task<List<Menu>> GetMenu(int groupId, string language, CancellationToken token = default)
    {
        return await StaticMainDA.GetMenu(conLocal, groupId, language, token);
    }
    public async Task<string> GetNews(CancellationToken token = default)
    {
        return await StaticMainDA.GetNews(conLocal, token);
    }
    public async Task<List<MenuToolBar>> GetToolbar(int groupId, string language, CancellationToken token = default)
    {
        return await StaticMainDA.GetToolBar(conLocal, groupId, language, token);
    }
    public async Task<List<TaxType>> GetTaxTypes(CancellationToken token = default)
    {
        return await StaticMainDA.GetTaxTypes(conLocal, token);
    }
    public async Task<StockItemView?> GetStockViewPerLine(StockViewPar par, CancellationToken token = default)
    {
        return await StaticMainDA.GetStockViewPerLine(conLocal, par, token);
    }
    public async Task<List<Unit>> GetUnitsByItemId(string itemId, CancellationToken token = default)
    {
        return await StaticMainDA.GetUnitsByItemId(conLocal, itemId, token);
    }
    public async Task<int> ItemTransactionsCount(string itemId, CancellationToken token = default)
    {
        return await StaticMainDA.ItemTransactionsCount(conLocal, itemId, token);
    }
    public async Task ItemDelete(string itemId, CancellationToken token = default)
    {
        await StaticMainDA.ItemDelete(conLocal, itemId, token);
    }
    public async Task<DataTableJson?> ItemsList(CancellationToken token = default)
    {
        return await StaticMainDA.ItemsList(conLocal, token);
    }
    public async Task<DataTableJson?> ItemsByCategory(string categoryId, CancellationToken token = default)
    {
        return await StaticMainDA.ItemsByCategory(conLocal, categoryId, token);
    }
    public async Task<DataTableJson?> CategoriesDT(CancellationToken token = default)
    {
        return await StaticMainDA.CategoriesDT(conLocal, token);
    }
    public async Task<int> CategoryItemsCount(string categoryId, CancellationToken token = default)
    {
        return await StaticMainDA.CategoryItemsCount(conLocal, categoryId, token);
    }
    public async Task<int> UnitIdTransactionCount(int unitId, CancellationToken token = default)
    {
        return await StaticMainDA.UnitIdTransactionCount(conLocal, unitId, token);
    }
    public async Task<DataTableJson?> PaymentDueDateTotal(int voucherTypeId, DateTime from, DateTime to, int financialYear, int branchId, CancellationToken token = default)
    {
        return await StaticMainDA.PaymentDueDateTotal(conLocal, voucherTypeId, from, to, financialYear, branchId, token);
    }
    public async Task<DataTableJson?> PaymentDueDate(int voucherTypeId, DateTime from, DateTime to, int reportType, int branchId, CancellationToken token = default)
    {
        return await StaticMainDA.PaymentDueDate(conLocal, voucherTypeId, from, to, reportType, branchId, token);
    }
    public async Task<DataTableJson?> ItemsBySupplier(string customerId, DateTime from, DateTime to, CancellationToken token = default)
    {
        return await StaticMainDA.ItemsBySupplier(conLocal, customerId, from, to, token);
    }
    public async Task<DataTableJson?> SuppliersByItem(string itemId, DateTime from, DateTime to, CancellationToken token = default)
    {
        return await StaticMainDA.SuppliersByItem(conLocal, itemId, from, to, token);
    }
    public async Task<DataTableJson?> StockMostMoving(int BranchID, int VoucherTypeID, int ReportType, int nRecords, DateTime from, DateTime to, string refNo, CancellationToken token = default)
    {
        return await StaticMainDA.StockMostMoving(conLocal, BranchID, VoucherTypeID, ReportType, nRecords, from, to, refNo, token);
    }
    public async Task<DataTableJson?> StockMostProfit(int BranchID, int ReportType, int nRecords, DateTime from, DateTime to, string refNo, CancellationToken token = default)
    {
        return await StaticMainDA.StockMostProfit(conLocal, BranchID, ReportType, nRecords, from, to, refNo, token);
    }
    public async Task<DataTableJson?> StockTakeDates(int branchId, int financialYear, CancellationToken token = default)
    {
        return await StaticMainDA.StockTakeDates(conLocal, branchId, financialYear, token);
    }
    public async Task<CustomerInfo?> GetCustomerInfo(int financialYear, int branchId, string customerId, CancellationToken token = default)
    {
        return await StaticMainDA.GetCustomerInfo(conLocal, financialYear, branchId, customerId, token);
    }
    public async Task<DataTableJson?> RestaurantProductsList(CancellationToken token = default)
    {
        return await StaticMainDA.RestaurantProductsList(conLocal, token);
    }
    public async Task<DataTableJson?> RawMaterialsByProductId(string productId, CancellationToken token = default)
    {
        return await StaticMainDA.RawMaterialsByProductId(conLocal, productId, token);
    }
    public async Task<Invoice?> InvoiceByTransactionId(string transactionId, string userName, string language, CancellationToken token = default)
    {
        return await StaticMainDA.InvoiceByTransactionId(conLocal, transactionId, userName, language, token);
    }
    public async Task DeleteTransaction(string transactionId, CancellationToken token = default)
    {
        await StaticMainDA.DeleteTransaction(conLocal, transactionId, token);
    }
    public async Task<bool> UserHasRight(int rightType, int userId, int groupId, string menuId, int rightId, CancellationToken token = default)
    {
        return await StaticMainDA.UserHasRight(conLocal, rightType, userId, groupId, menuId, rightId, token);
    }
    public async Task<DataTableJson?> CashAccountsList(CancellationToken token = default)
    {
        return await StaticMainDA.CashAccountsList(conLocal, token);
    }
    public async Task<DataTableJson?> BankAccountsList(CancellationToken token = default)
    {
        return await StaticMainDA.BankAccountsList(conLocal, token);
    }
    public async Task<DataTableJson?> GetAllBranches(string language, int all, CancellationToken token = default)
    {
        return await StaticMainDA.GetAllBranches(conLocal, language, all, token);
    }
    public async Task<List<VoucherNavation>> GetVoucherNavigations(int year, int branchId, int voucherTypeId, CancellationToken token = default)
    {
        return await StaticMainDA.GetVoucherNavigations(conLocal, year, branchId, voucherTypeId, token);
    }
    public async Task<VoucherNavation?> GetVoucherNavigationByVoucherNo(string voucherNo, CancellationToken token = default)
    {
        return await StaticMainDA.GetVoucherNavigationByVoucherNo(conLocal, voucherNo, token);
    }
    public async Task<VoucherMain?> SaveVoucher(VoucherMain voucher, CancellationToken token = default)
    {
        return await StaticMainDA.SaveVoucher(conLocal, voucher, token);
    }
    public async Task<VoucherMain?> GetVoucherById(string voucherNo, CancellationToken token = default)
    {
        return await StaticMainDA.GetVoucherById(conLocal, voucherNo, token);
    }
    public async Task<List<VoucherMain>> VouchersByDateRange(int branchId, int voucherTypeId, DateTime from, DateTime to, CancellationToken token = default)
    {
        return await StaticMainDA.VouchersByDateRange(conLocal, branchId, voucherTypeId, from, to, token);
    }
    public async Task<DataTableJson?> VoucherSearchDT(VoucherSearchPar par, CancellationToken token = default)
    {
        return await StaticMainDA.VoucherSearchDT(conLocal, par, token);
    }
    public async Task<DataTableJson?> VoucherDetailByVoucherNoDt(string voucherNo, CancellationToken token = default)
    {
        return await StaticMainDA.VoucherDetailByVoucherNoDt(conLocal, voucherNo, token);
    }
    public async Task<string> GetNextVoucherNo(int financialYear, int branchId, int voucherTypeId, CancellationToken token = default)
    {
        return await StaticMainDA.GetNextVoucherNo(conLocal, financialYear, branchId, voucherTypeId, token);
    }
    public async Task<FooterInfo?> GetItemFooterInfo(string itemId, CancellationToken token = default)
    {
        return await StaticMainDA.GetItemFooterInfo(conLocal, itemId, token);
    }
    public async Task<List<ContractItem>?> GetItemsByContractID(string contractId, CancellationToken token = default)
    {
        return await StaticMainDA.GetItemsByContractID(conLocal, contractId, token);
    }
    public async Task<decimal> GetFixedPercentage(string customerId, string itemId, CancellationToken token = default)
    {
        return await StaticMainDA.GetFixedPercentage(conLocal, customerId, itemId, token);
    }
    public async Task<string> GetNextTransactionId(int year, int branchId, int voucherTypeId, CancellationToken token = default)
    {
        return await StaticMainDA.GetNextTransactionId(conLocal, year, branchId, voucherTypeId, token);
    }
    public async Task<DateTime> GetServerDate(CancellationToken token = default)
    {
        return await StaticMainDA.GetServerDate(conLocal, token);
    }
    public async Task<List<TransactionNavigation>> GetTransactionNavigations(int year, int voucherTypeId, int branchId, CancellationToken token = default)
    {
        return await StaticMainDA.GetTransactionNavigations(conLocal, year, voucherTypeId, branchId, token);
    }
    public async Task<string> CheckForPreviousReturn(string transactionId, CancellationToken token = default)
    {
        return await StaticMainDA.CheckForPreviousReturn(conLocal, transactionId, token);
    }
    public async Task<DataTableJson?> CustomersSuppliersList(bool withEmptyRow, CancellationToken token = default)
    {
        return await StaticMainDA.CustomersSuppliersList(conLocal, withEmptyRow, token);
    }
    public async Task<string> ValidateZatcaCustomer(string customerId, string vatNo, string language, CancellationToken token = default)
    {
        return await StaticMainDA.ValidateZatcaCustomer(conLocal, customerId, vatNo, language, token);
    }
    public async Task<List<ContractCostingMain>> GetCostingByTransactionId(string transactionId, CancellationToken token = default)
    {
        return await StaticMainDA.GetCostingByTransactionId(conLocal, transactionId, token);
    }
    public async Task<DataTableJson?> VoucherTypesDT(int selectType, string language, CancellationToken token = default)
    {
        return await StaticMainDA.VoucherTypesDT(conLocal, selectType, language, token);
    }
    public async Task CreateSqlUser(string dbName, string userName, string password, CancellationToken token = default)
    {
        await StaticMainDA.CreateSqlUser(conLocal, dbName, userName, password, token);
    }
    public async Task<DataTableJson?> TransSearch(int year, int branchId, int voucherTypeId, DateTime from, DateTime to, bool isTemp, string searchTerm, int searchMethod, CancellationToken token = default)
    {
        return await StaticMainDA.TransSearch(conLocal, year, branchId, voucherTypeId, from, to, isTemp, searchTerm, searchMethod, token);
    }
    public async Task<bool> IsPeriodClosed(int branchId, DateTime date, CancellationToken token=default)
    {
        return await StaticMainDA.IsPeriodClosed(conLocal, branchId, date, token);
    }
    public async Task<DataTableJson?> GetPaymentMethods(string lang, CancellationToken token = default)
    {
        return await StaticMainDA.GetPaymentMethods(conLocal, lang, token);
    }
    public async Task<List<ParentAccount>> GetParentChildAccounts(CancellationToken token = default)
    {
        return await StaticMainDA.GetParentChildAccounts(conLocal, token);
    }
    public async Task<bool> IsValidAccountNo(string accountNo, CancellationToken token = default)
    {
        return await StaticMainDA.IsValidAccountNo(conLocal, accountNo, token);
    }
    public async Task<DataTableJson?> AccountsListWithHierarchy(CancellationToken token = default)
    {
        return await StaticMainDA.AccountsListWithHierarchy(conLocal, token);
    }
    public async Task<StatementMaster> AccountStatement(AcStatementPar par, CancellationToken token = default)
    {
        return await StaticMainDA.AccountStatement(conLocal, par, token);
    }
    public async Task<DataTableJson?> GetList(int typeId, int userId, CancellationToken token = default)
    {
        return await StaticMainDA.GetList(conLocal, typeId, userId, token);
    }
    public async Task<List<ItemCardCategory>> GetItemCardCategories(int typeId, CancellationToken token = default)
    {
        return await StaticMainDA.GetItemCardCategories(conLocal, typeId, token);
    }
    public  IAsyncEnumerable<ItemCard?> GetItemsByCategory(string categoryId, CancellationToken token = default)
    {
         return StaticMainDA.GetItemsByCategory(conLocal, categoryId, token);
    }
    public IAsyncEnumerable<ParentAccount?> GetParentAccounts(CancellationToken token = default)
    {
        return StaticMainDA.GetParentAccounts(conLocal, token);
    }
    public IAsyncEnumerable<ChildAccount?> GetChildAccounts(CancellationToken token = default)
    {
        return StaticMainDA.GetChildAccounts(conLocal, token);
    }
}
