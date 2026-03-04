using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using Microsoft.Extensions.Logging;
using MurshisoftData;
using MurshisoftData.Models;
using System.Threading.Channels;

namespace Api.Controllers;

[Route("[controller]/[action]")]
public class TransactionController(ILogger<TransactionController> logger, SqlServerDb db, Channel<SyncTransJob> channel, Channel<SyncStockJob> channelStock) : ControllerBase
{
    [HttpGet("{language}")]
    public async Task<IActionResult> GetVoucherTypes(string language)
    {
        var result = await db.GetVoucherTypes(language, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> GetPosCustomers()
    {
        var result = await db.GetPosCustomers();
        return Ok(result);
    }

    [HttpGet("{userName}")]
    public async Task<IActionResult> GetPosCategories(string userName)
    {
        var result = await db.GetPosCategories(userName, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpGet("{userName}")]
    public async Task<IActionResult> OutstandingsByUser(string userName)
    {
        var result = await db.OutstandingsByUser(userName, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> GetPosUsers()
    {
        var result = await db.GetPosUsers( HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{mobileNo}")]
    public async Task<IActionResult> GetSalesCustomerByMobileNo(string mobileNo)
    {
        var result = await db.GetSalesCustomerByMobileNo(mobileNo, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{userName}")]
    public async Task<IActionResult> GetByCashierUserName(string userName)
    {
        var result = await db.GetByCashierUserName(userName, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{branchId}")]
    public async Task<IActionResult> LoadPrinters(int branchId)
    {
        var result = await db.LoadPrinters(branchId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{branchId}")]
    public async Task<IActionResult> GetDepartmentPrinters(int branchId)
    {
        var result = await db.GetDepartmentPrinters(branchId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> GetSpanTypes()
    {
        var result = await db.GetSpanTypes(HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{rawMaterialId}")]
    public async Task<IActionResult> GetChildListByRawMaterial(string rawMaterialId)
    {
        var result = await db.GetChildListByRawMaterial(rawMaterialId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{providerId}/{branchId}")]
    public async Task<IActionResult> LoadMenuItems(int providerId, int branchId)
    {
        var result = await db.LoadMenuItems(providerId, branchId, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpGet("{branchId}")]
    public async Task<IActionResult> GetCategoriesWithAddons(int branchId)
    {
        var result = await db.GetCategoriesWithAddons(branchId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{branchId}")]
    public async Task<IActionResult> GetHallsAndTables(int branchId)
    {
        var result = await db.GetHallsAndTables(branchId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet]
    public async Task<IActionResult> GetProviders()
    {
        var data = await db.GetProviders(HttpContext.RequestAborted);
        return Ok(data);
    }
    [HttpGet("{branchId}/{voucherTypeId}/{userName}")]
    public async Task<IActionResult> SearchTemp(int branchId,int voucherTypeId, string userName)
    {
        var data = await db.SearchTemp(branchId, voucherTypeId, userName);
        return Ok(data);
    }
    [HttpGet("{itemId}")]
    public async Task<IActionResult> ItemExists(string itemId)
    {
        var result = await db.ItemExists(itemId, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpGet("{itemId}")]
    public async Task<IActionResult> GetStockById(string itemId)
    {
        var data = await db.GetStockById(itemId, HttpContext.RequestAborted);
        return Ok(data);
    }

    [HttpGet("{branchId}/{userName}")]
    public async Task<IActionResult> GetLastIds(int branchId, string userName)
    {
        var data = await db.GetLastIds(branchId, userName);
        return Ok(data);
    }
    [HttpGet("{financialYear}/{branchId}/{voucherTypeId}/{searchMethod}/{language}")]
    public async Task<IActionResult> GetStockList(int financialYear, int branchId, int voucherTypeId, int searchMethod, string language)
    {
        var data = await db.GetStockList(financialYear, branchId, voucherTypeId, searchMethod, language);
        return Ok(data);
    }

    [HttpGet("{financialYear}/{branchId}/{lastInventoryId}")]
    public async Task<IActionResult> GetUpdatedBalance(int financialYear, int branchId, int lastInventoryId)
    {
        var data = await db.GetUpdatedBalance(financialYear, branchId, lastInventoryId);
        return Ok(data);
    }
    [HttpGet("{financialYear}/{itemid}")]
    public async Task<IActionResult> GetBranchBalance(int financialYear, string itemid)
    {
        var data = await db.GetBranchBalance(financialYear, itemid);
        return Ok(data);
    }
    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        var data = await db.GetCategories(HttpContext.RequestAborted);
        return Ok(data);
    }
    [HttpGet]
    public async Task<IActionResult> GetCategoriesRestaurant()
    {
        var data = await db.GetCategoriesRestaurant(HttpContext.RequestAborted);
        return Ok(data);
    }
    [HttpGet("{branchId}/{language}")]
    public async Task<IActionResult> GetPaymentTypes(int branchId, string language)
    {
        var data = await db.GetPaymentTypes(branchId, language);
        return Ok(data);
    }
    [HttpGet("{branchId}/{typeId}")]
    public async Task<IActionResult> UsersList(int branchId, int typeId)
    {
        var users=await db.UsersList(branchId, typeId);
        return Ok(users);
    }
    [HttpGet("{loginId}")]
    public async Task<IActionResult> GetSalesByLogin(int loginId)
    {
        var sales = await db.GetSalesByLogin(loginId, HttpContext.RequestAborted);
        return Ok(sales);
    }
    [HttpGet("{loginId}")]
    public async Task<IActionResult> LogOff(int loginId)
    {
        await db.LogOff(loginId, HttpContext.RequestAborted);
        return Ok();
    }
    [HttpGet("{barcode}")]
    public async Task<IActionResult> SearchByBarcode(string barcode)
    {
        var items = await db.SearchByBarcode(barcode, HttpContext.RequestAborted);
        return Ok(items);
    }
    [HttpGet("{userId}")]
    public async Task<IActionResult> GetCustomers(int userId)
    {
        var items = await db.GetCustomers(userId, HttpContext.RequestAborted);
        return Ok(items);
    }
    [HttpPost]
    public async Task<IActionResult> SaveCategory([FromBody] ItemCard item)
    {
        var categoryId = await db.SaveCategory(item, HttpContext.RequestAborted);
        if (categoryId != null)
        {
            await channelStock.Writer.WriteAsync(new SyncStockJob(categoryId));
            return Ok(categoryId);
        }
        return BadRequest();
    }
    [HttpPost]
    public async Task<IActionResult> SaveStock([FromBody] ItemCard item)
    {
        var stock=await db.SaveStock(item, HttpContext.RequestAborted);
        if (stock != null)
        {
            await channelStock.Writer.WriteAsync(new SyncStockJob(stock.ItemID));
            return Ok(stock);
        }
        return BadRequest();
    }
    [HttpPost]
    public async Task<IActionResult> SaveTransaction([FromBody] TransactionMain transaction)
    {
        if (transaction != null)
        {
            for (int i = 0; i < transaction.TransactionDetails.Count; i++)
            {
                transaction.TransactionDetails[i].FooterInfo = null;
                transaction.TransactionDetails[i].StockItemView = null;
                transaction.CustomerVat ??= "";
            }
            var id = await db.SaveTransaction(transaction, HttpContext.RequestAborted);
            await channel.Writer.WriteAsync(new SyncTransJob(id));
            return Ok(id);
        }
       return BadRequest();
    }
    [HttpPost]
    public async Task<IActionResult> SaveTransactionTemp([FromBody] TransactionMain transaction)
    {
        if (transaction != null)
        {
            for (int i = 0; i < transaction.TransactionDetails.Count; i++)
            {
                transaction.TransactionDetails[i].FooterInfo = null;
                transaction.TransactionDetails[i].StockItemView = null;
            }
            var id = await db.SaveTransactionTemp(transaction, HttpContext.RequestAborted);
            return Ok(id);
        }
        return BadRequest();
    }
    [HttpPost]
    public async Task<IActionResult> Login([FromBody] LoginData login)
    {
        var result= await db.UserLogin(login, HttpContext.RequestAborted);
        if (result is null)
        {
            return BadRequest("Invalid User name or password");
        }
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> GetNextIds([FromBody] NextIdParam login)
    {
        var result = await db.GetNextIds(login, HttpContext.RequestAborted);
        if (result is null)
        {
            return BadRequest("Invalid params");
        }
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> GetPosSingleItem([FromBody] PosItemPar par)
    {
        var result = await db.UnitDetailSingleByBarcode(par, HttpContext.RequestAborted);
        if (result is null)
        {
            return BadRequest("Invalid params");
        }
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> GetPosMultipleItem([FromBody] PosItemPar par)
    {
        var result = await db.UnitDetailByBarcode(par, HttpContext.RequestAborted);
        if (result is null)
        {
            return BadRequest("Invalid params");
        }
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> IsCreditAllowed([FromBody] CreditLimitPar par)
    {
        var result = await db.IsCreditAllowed(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> SearchInvoices([FromBody] InvoiceSearchParam par)
    {
        var result = await db.SearchInvoices(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> XReport([FromBody] XReportParam par)
    {
        var result = await db.XReport(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> XReportByCategory([FromBody] XReportByCategoryParam par)
    {
        var result = await db.XReportByCategory(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> CloseSales([FromBody] CloseSalesParam par)
    {
        var result = await db.CloseSales(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> SalesClosingReport([FromBody] ClosingReportPar par)
    {
        var result = await db.GetClosingListByDate(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    [HttpPost]
    public async Task<IActionResult> GetInvoice([FromBody] InvoicePrintParam par)
    {
        var result = await db.GetInvoice(par, HttpContext.RequestAborted);
        return Ok(result);
    }
    
    [HttpPost]
    public async Task<IActionResult> UpdateCancelledItem([FromBody] ItemDeletePar item)
    {
        await db.UpdateCancelledItem(item, HttpContext.RequestAborted);
        return Ok();
    }
    [HttpPost]
    public async Task<IActionResult> UpdateClearScreen([FromBody] TransactionMain tr)
    {
        await db.UpdateClearScreen(tr, HttpContext.RequestAborted);
        return Ok();
    }
    [HttpPost]
    public async Task<IActionResult> SelectTransactionByID([FromBody] TransactioinByIdPar par)
    {
       var data= await db.SelectTransactionByID(par, HttpContext.RequestAborted);
        return Ok(data);
    }
    [HttpPost]
    public async Task<IActionResult> RestInvoiceData([FromBody] RestInvoiceParm par)
    {
        var data = await db.RestInvoiceData(par, HttpContext.RequestAborted);
        return Ok(data);
    }
    [HttpPost]
    public async Task<IActionResult> SavePrinter([FromBody] DepartmentPrinter par)
    {
        var data = await db.SavePrinter(par, HttpContext.RequestAborted);
        return Ok(data);
    }
    [HttpPost]
    public async Task<IActionResult> UpdatePosCustomer([FromBody] SalesCustomer customer)
    {
        var data = await db.UpdatePosCustomer(customer, HttpContext.RequestAborted);
        return Ok(data);
    }
    [HttpPost]
    public async Task<IActionResult> CustomersSalesReport([FromBody] CustomerReportPar par)
    {
        var data = await db.CustomersSalesReport(par, HttpContext.RequestAborted);
        return Ok(data);
    }
    [HttpPost]
    public async Task<IActionResult> UpdateSpanResponse([FromBody] SpanResponseData par)
    {
        await db.UpdateSpanResponse(par, HttpContext.RequestAborted);
        return Ok();
    }
    [HttpPost]
    public async Task<IActionResult> SavePosClosing([FromBody] PosSalesClosingMaster par)
    {
        var id= await db.SavePosClosing(par, HttpContext.RequestAborted);
        return Ok(id);
    }
   
}
