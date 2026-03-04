using Api.Services;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using Microsoft.Extensions.Logging;
using MurshisoftData;
using MurshisoftData.Main;
using MurshisoftData.Models;
using MurshisoftData.Models.Main;
using System.Threading.Channels;

namespace Api.Controllers;

[Route("[controller]/[action]")]
public class MainController(ILogger<TransactionController> logger, SqlServerDb db, Channel<SyncTransJob> channel, Channel<SyncStockJob> channelStock) : ControllerBase
{
    
    [HttpGet]
    public async Task<IActionResult> GetFinancialYears()
    {
        var result = await db.GetFinancialYears(HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpGet("{parentId}/{divisionId}")]
    public async Task<IActionResult> GetBranches(int parentId, int divisionId)
    {
        var result = await db.GetBranches(parentId, divisionId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{financialYear}/{branchId}")]
    public async Task<IActionResult> GetLastStockTakeDate(int financialYear, int branchId)
    {
        var result = await db.GetLastStockTakeDate(financialYear, branchId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{groupId}/{language}")]
    public async Task<IActionResult> GetMenu(int groupId, string language)
    {
        var result = await db.GetMenu(groupId, language, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> GetNews()
    {
        var result = await db.GetNews(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{groupId}/{language}")]
    public async Task<IActionResult> GetToolbar(int groupId, string language)
    {
        var result = await db.GetToolbar(groupId, language, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> GetTaxTypes()
    {
        var result = await db.GetTaxTypes(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> GetStockViewPerLine([FromBody] StockViewPar par)
    {
        var result= await db.GetStockViewPerLine(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{itemId}")]
    public async Task<IActionResult> GetUnitsByItemId(string itemId)
    {
        var result = await db.GetUnitsByItemId(itemId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{itemId}")]
    public async Task<IActionResult> ItemTransactionsCount(string itemId)
    {
        var result = await db.ItemTransactionsCount(itemId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{itemId}")]
    public async Task<IActionResult> ItemDelete(string itemId)
    {
         await db.ItemDelete(itemId, HttpContext.RequestAborted);
        return Ok();
    }
    [HttpGet]
    public async Task<IActionResult> ItemsList()
    {
        var result = await db.ItemsList(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{categoryId}")]
    public async Task<IActionResult> ItemsByCategory(string categoryId)
    {
        var result=await db.ItemsByCategory(categoryId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> CategoriesDT()
    {
        var result = await db.CategoriesDT(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{categoryId}")]
    public async Task<IActionResult> CategoryItemsCount(string categoryId)
    {
        var result = await db.CategoryItemsCount(categoryId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{unitId}")]
    public async Task<IActionResult> UnitIdTransactionCount(int unitId)
    {
        var result = await db.UnitIdTransactionCount(unitId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> PaymentDueDateTotal([FromBody] DuReportParam par)
    {
        var result = await db.PaymentDueDateTotal(par.voucherTypeId, par.from, par.to, par.financialYear, par.branchId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> PaymentDueDate([FromBody] DuReportParam par)
    {
        var result = await db.PaymentDueDate(par.voucherTypeId, par.from, par.to, par.financialYear, par.branchId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> ItemsBySupplier([FromBody] ItemBySupplierPar par)
    {
        var result = await db.ItemsBySupplier(par.customerId, par.from, par.to, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> SuppliersByItem([FromBody] SupplierByItemPar par)
    {
        var result = await db.SuppliersByItem(par.itemId, par.from, par.to, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> StockMostMoving([FromBody] StockMostMovingPar par)
    {
        var result = await db.StockMostMoving(par.BranchID, par.VoucherTypeID, par.ReportType, par.nRecords, par.from, par.to, par.refNo, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> StockMostProfit([FromBody] StockMostProfitPar par)
    {
        var result = await db.StockMostProfit(par.BranchID, par.ReportType, par.nRecords, par.from, par.to, par.refNo, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{branchId}/{financialYear}")]
    public async Task<IActionResult> StockTakeDates(int branchId, int financialYear)
    {
        var result = await db.StockTakeDates(branchId, financialYear, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> GetCustomerInfo([FromBody] CustomerInfoPar par)
    {
        var result = await db.GetCustomerInfo(par.financialYear, par.branchId, par.customerId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> RestaurantProductsList()
    {
        var result = await db.RestaurantProductsList(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{productId}")]
    public async Task<IActionResult> RawMaterialsByProductId(string productId)
    {
        var result = await db.RawMaterialsByProductId(productId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> InvoiceByTransactionId([FromBody] InvoiceParam par)
    {
        var result = await db.InvoiceByTransactionId(par.transactionId, par.userName, par.language, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> DeleteTransaction([FromBody] TransactionIdPar par)
    {
        await db.DeleteTransaction(par.transactionId, HttpContext.RequestAborted);
        return Ok();
    }
    [HttpPost]
    public async Task<IActionResult> UserHasRight([FromBody] UserRightPar par)
    {
        var result = await db.UserHasRight(par.rightType, par.userId,par.groupId, par.menuId, par.rightType, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> CashAccountsList()
    {
        var result = await db.CashAccountsList(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> BankAccountsList()
    {
        var result = await db.BankAccountsList(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{language}/{all}")]
    public async Task<IActionResult> GetAllBranches(string language, int all)
    {
        var result = await db.GetAllBranches(language, all, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{year}/{branchId}/{voucherTypeId}")]
    public async Task<IActionResult> GetVoucherNavigations(int year, int branchId, int voucherTypeId)
    {
        var result = await db.GetVoucherNavigations(year, branchId, voucherTypeId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> GetVoucherNavigationByVoucherNo([FromBody] VoucherNoPar par)
    {
        var result = await db.GetVoucherNavigationByVoucherNo(par.voucherNo, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> SaveVoucher([FromBody] VoucherMain par)
    {
        var result = await db.SaveVoucher(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> GetVoucherById([FromBody] VoucherNoPar par)
    {
        var result = await db.GetVoucherById(par.voucherNo, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> VouchersByDateRange([FromBody] VouchersByDateRangePar par)
    {
        var result = await db.VouchersByDateRange(par.branchId, par.voucherTypeId, par.from, par.to, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> VoucherSearchDT([FromBody] VoucherSearchPar par)
    {
        var result = await db.VoucherSearchDT(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> VoucherDetailByVoucherNoDt([FromBody] VoucherNoPar par)
    {
        var result = await db.VoucherDetailByVoucherNoDt(par.voucherNo, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{year}/{branchId}/{voucherTypeId}")]
    public async Task<IActionResult> GetNextVoucherNo(int year, int branchId, int voucherTypeId)
    {
        var result = await db.GetNextVoucherNo(year, branchId, voucherTypeId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{itemId}")]
    public async Task<IActionResult> GetItemFooterInfo(string itemId)
    {
        var result = await db.GetItemFooterInfo(itemId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> GetItemsByContractID([FromBody] ContractIdPar par)
    {
        var result = await db.GetItemsByContractID(par.contractId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> GetFixedPercentage([FromBody] CustomerWithItemIdPar par)
    {
        var result = await db.GetFixedPercentage(par.customerId, par.itemId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{year}/{branchId}/{voucherTypeId}")]
    public async Task<IActionResult> GetNextTransactionId(int year, int branchId, int voucherTypeId)
    {
        var result = await db.GetNextTransactionId(year, branchId, voucherTypeId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> GetServerDate()
    {
        var result = await db.GetServerDate(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{year}/{voucherTypeId}/{branchId}")]
    public async Task<IActionResult> GetTransactionNavigations(int year, int voucherTypeId, int branchId)
    {
        var result = await db.GetTransactionNavigations(year, voucherTypeId, branchId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> CheckForPreviousReturn([FromBody] TransactionIdPar par)
    {
        var result = await db.CheckForPreviousReturn(par.transactionId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{withEmptyRow}")]
    public async Task<IActionResult> CustomersSuppliersList(bool withEmptyRow)
    {
        var result = await db.CustomersSuppliersList(withEmptyRow, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> ValidateZatcaCustomer([FromBody] ZatcaCustomerValidationPar par)
    {
        var result = await db.ValidateZatcaCustomer(par.customerId, par.vatNo, par.language, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{selectType}/{language}")]
    public async Task<IActionResult> VoucherTypesDT(int selectType, string language)
    {
        var result = await db.VoucherTypesDT(selectType, language, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> CreateSqlUser([FromBody] CreateSqlUserPar par)
    {
        await db.CreateSqlUser(par.dbName,par.userName, par.password, HttpContext.RequestAborted);
        return Ok();
    }
    [HttpPost]
    public async Task<IActionResult> TransSearch([FromBody] TranSearchPar par)
    {
        var result = await db.TransSearch(par.year, par.branchId, par.voucherTypeId, par.from, par.to, par.isTemp, par.searchTerm, par.searchMethod, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> IsPeriodClosed([FromBody] PeriodClosePar par)
    {
        var result = await db.IsPeriodClosed(par.branchId, par.date, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{lang}")]
    public async Task<IActionResult> GetPaymentMethods(string lang)
    {
        var items = await db.GetPaymentMethods(lang, HttpContext.RequestAborted);
        return Ok(items);
    }
    [HttpGet]
    public async Task<IActionResult> GetParentChildAccounts()
    {
        var result = await db.GetParentChildAccounts(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> IsValidAccountNo([FromBody] AccountNoPar par)
    {
        var result = await db.IsValidAccountNo(par.accountNo, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> AccountsListWithHierarchy()
    {
        var result = await db.AccountsListWithHierarchy(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> AccountStatement([FromBody] AcStatementPar par)
    {
        var result = await db.AccountStatement(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{typeId}/{userId}")]
    public async Task<IActionResult> GetList(int typeId, int userId)
    {
        var result = await db.GetList(typeId, userId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{typeId}")]
    public async Task<IActionResult> GetItemCardCategories(int typeId)
    {
        var result = await db.GetItemCardCategories(typeId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{categoryId}")]
    public IActionResult GetItemsByCategory(string categoryId)
    {
        var result = db.GetItemsByCategory(categoryId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public IActionResult GetParentAccounts()
    {
        var result = db.GetParentAccounts(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public IActionResult GetChildAccounts()
    {
        var result = db.GetChildAccounts(HttpContext.RequestAborted);
        return Ok(result);
    }
}
