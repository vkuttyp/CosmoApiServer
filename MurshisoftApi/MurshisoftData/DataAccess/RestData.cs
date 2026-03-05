using MurshisoftData.Models;
using MurshisoftData.Models.POS;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurshisoftData.DataAccess;

public class RestData(string conLocal, bool useApi, string url)
{
    private readonly HttpClient httpClient = MyHttpClient.GetHttpClient(url);
    public  async Task<List<User>> UsersList(int branchId, int typeId)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/UsersList/{branchId}/{typeId}");
            return await MyCommand.ResponseToData<List<User>>(response) ?? [];
        }
        return await StaticPOSDA.UsersList(conLocal, branchId, typeId);
    }
    public  async Task<(DataTable, string)> RestInvoiceData(string procName, string transactionID, int branchID = 0, int invoiceHeadingID = 0, int depPrinterId = 0)
    {
        RestInvoicePrintResult? data = null;
        if (useApi)
        {
            var param = new { procName, transactionID, branchID, invoiceHeadingID, depPrinterId };
            var json = JsonSerializer.Serialize(param);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/RestInvoiceData", val);
            data = await MyCommand.ResponseToData<RestInvoicePrintResult>(response);
        }
        else
        {
            var par = new RestInvoiceParm(procName, transactionID, branchID, invoiceHeadingID, depPrinterId);
            data = await StaticPOSDA.RestInvoiceData(conLocal, par);
        }
        return ConvertInvData(transactionID, data);
    }
    (DataTable, string) ConvertInvData(string transactionId, RestInvoicePrintResult data) 
    {
        var ser = new Serializer();
        DataTable table = ser.DeserializeJson<SerializableDataTable>(data.table).ToDataTable();
        table.Columns.Add("ReturnBarcode", typeof(string));
        table.Rows[0]["ReturnBarcode"] = MyHelpers.GetReturnBarcode(transactionId);
        return (table, data.QrCode);
    }
    public  async Task<List<DepartmentPrinter>> GetDepartmentPrinters(int branchId)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetDepartmentPrinters/{branchId}");
            return await MyCommand.ResponseToData<List<DepartmentPrinter>>(response) ?? [];
        }
        return await StaticPOSDA.GetDepartmentPrinters(conLocal, branchId);
    }

     DataSet GetDs(InvoicePrintResult data, string transactionId) 
    {
        var ser = new Serializer();

        DataTable header = ser.DeserializeJson<SerializableDataTable>(data.Header).ToDataTable();
        DataTable details = ser.DeserializeJson<SerializableDataTable>(data.Details).ToDataTable();
        header.Rows[0]["QRCode"] = data.QrCode;
        header.Columns.Add("ReturnBarcode", typeof(string));
        header.Rows[0]["ReturnBarcode"] = MyHelpers.GetReturnBarcode(transactionId);
        var dataset = new DataSet("Invoice");
        dataset.Tables.Add(header);
        dataset.Tables.Add(details);
        return dataset;
    }
    public async Task<DataSet?> GetInvoiceDataSet(string invoiceNo, int year, string language)
    {
        InvoicePrintResult? data = null;
        InvoicePrintParam par = new InvoicePrintParam(invoiceNo, year, language);

        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/GetInvoice", val);
            data = await MyCommand.ResponseToData<InvoicePrintResult>(response);
        }
        else data = data = await StaticPOSDA.GetInvoice(conLocal, par);
        if(data!=null)
        return GetDs(data, invoiceNo);
        return null;
    }
    public  async Task<int> SavePrinter(DepartmentPrinter printer)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(printer);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/SavePrinter", val);
            return await MyCommand.ResponseToInt(response);
        }
        return await StaticPOSDA.SavePrinter(conLocal, printer);
    }
    public  async Task<List<DepartmentPrinter>> LoadPrinters(int branchId)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/LoadPrinters/{branchId}");
            return await MyCommand.ResponseToData<List<DepartmentPrinter>>(response) ?? [];
        }
        return await StaticPOSDA.LoadPrinters(conLocal, branchId) ?? [];
    }
    static List<RestaurantCategory>? _categories =null;
    public  async Task<List<RestaurantCategory>> GetRestanrantCategories(int branchId)
    {
        if (_categories == null)
        {
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Transaction/GetCategoriesWithAddons/{branchId}");
                _categories= await MyCommand.ResponseToData<List<RestaurantCategory>>(response) ?? [];
            }
            else
           _categories = await StaticPOSDA.GetCategoriesWithAddons(conLocal, branchId) ?? [];
        }
        return _categories;
    }
   

    static  List<ItemCategory>?  _catList;
    public  async Task<List<ItemCategory>> GetItemCategories()
    {
        if (_catList == null)
        {
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Transaction/GetCategories");
                _catList= await MyCommand.ResponseToData<List<ItemCategory>>(response) ?? [];
            }
            else
            _catList = await StaticPOSDA.GetCategories(conLocal);
        }
        return _catList;
    }
    public  async Task<List<BranchBalance>> GetBranchBalance(int financialYear, string itemId)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetBranchBalance/{financialYear}/{itemId}");
            return await MyCommand.ResponseToData<List<BranchBalance>>(response) ?? [];
        }
        return await StaticPOSDA.GetBranchBalance(conLocal, financialYear, itemId);
    }
    public  async Task<StockWithUpdatedBalance?> GetUpdatedBalance(int financialYear, int branchId, int lastInventoryId)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetUpdatedBalance/{financialYear}/{branchId}/{lastInventoryId}");
            return await MyCommand.ResponseToData<StockWithUpdatedBalance>(response);
        }
        return await StaticPOSDA.GetUpdatedBalance(conLocal, financialYear, branchId, lastInventoryId);
    }
    
    public  async Task<(int,DataTable?)> GetStockList(DataTable input, int lastId, int financialYear, int branchId, int voucherTypeId, int searchMethod, string language)
    {
        int rows=input?.Rows?.Count ?? 0;
        if (rows>0 && lastId>0)
        {
            var balance = await GetUpdatedBalance(financialYear, branchId, lastId);
            if (balance?.Items != null)
            {
                foreach (var item in balance.Items)
                {
                    var row = input.Select("ItemID='" + item.ItemId + "'").FirstOrDefault();
                    if (row != null)
                    {
                        row["Balance"] = item.Balance;
                        row["BalanceUnit"] = item.BalanceUnit;
                        row.EndEdit();
                        row.AcceptChanges();
                    }
                }
                return (balance.LastInventoryId, input);
            }
            return (lastId, input);
        }
        StockListWithLastInventory? data;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetStockList/{financialYear}/{branchId}/{voucherTypeId}/{searchMethod}/{language}");
             data = await MyCommand.ResponseToData<StockListWithLastInventory>(response);
        }
        else
            data = await StaticPOSDA.GetStockList(conLocal, financialYear, branchId, voucherTypeId, searchMethod, language);
       if(data?.Items==null) return (0,null); 
        return (data.LastInventoryId,ConvertToDataTable(data.Items));
    }
     DataTable ConvertToDataTable(List<StockItem> lst)
    {
        var table = new DataTable("Items");
        table.Columns.Add("ItemId", typeof(string));
        table.Columns.Add("PartNo", typeof(string));
        table.Columns.Add("RefNo", typeof(string));
        table.Columns.Add("ItemName", typeof(string));
        table.Columns.Add("ItemNameEnglish", typeof(string));
        table.Columns.Add("CostPrice", typeof(decimal));
        table.Columns.Add("SalesPrice", typeof(decimal));
        table.Columns.Add("LastPrice", typeof(decimal));
        table.Columns.Add("Location", typeof(string));

        lst.ForEach(x => table.Rows.Add(x.ItemId, x.PartNo, x.RefNo, x.ItemName,
            x.ItemNameEnglish, x.CostPrice, x.SalesPrice, x.LastPrice, x.Location));
        return table;
    }
    static List<VoucherType>? _voucherTypes;
    public  async Task<List<VoucherType>> GetVoucherTypes(string language)
    {
        if (_voucherTypes == null)
        {
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Transaction/GetVoucherTypes/{language}");
                _voucherTypes = await MyCommand.ResponseToData<List<VoucherType>>(response) ?? [];
            }
            else
            {
                _voucherTypes = await StaticPOSDA.GetVoucherTypes(conLocal, language);
            }
        }
        return _voucherTypes;
    }
   
    public async Task<ClosingMaster?> CloseSales(int financialYear, int branchId, int closingId, string userName, decimal amount, DateTime closingDate, DateTime closingTime, string notes = "")
    {
        var par = new CloseSalesParam(financialYear, branchId, closingId, userName, amount, closingDate, closingTime, notes);
        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/CloseSales", val);
            return await MyCommand.ResponseToData<ClosingMaster>(response);
        }
        return await StaticPOSDA.CloseSales(conLocal, par);
    }
   
    public async Task<List<ClosingMaster>> GetClosingListByDate(DateTime from, DateTime to)
    {
        var par = new ClosingReportPar(from, to);
        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/SalesClosingReport", val);
            return await MyCommand.ResponseToData<List<ClosingMaster>>(response) ?? [];
        }

        return await StaticPOSDA.GetClosingListByDate(conLocal, par);
    }
    
    static List<Account>? _customers;
    public  async Task<List<Account>> GetCustomers(int loginId)
    {
        if (_customers == null)
        {
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Transaction/GetCustomers/{loginId}");
                _customers= await MyCommand.ResponseToData<List<Account>>(response) ?? [];
            }
            else
                _customers = await StaticPOSDA.GetCustomers(conLocal, loginId);
        }
        return _customers;
    }
   
    public  async Task<bool> IsCreditAllowed(string customerId, int branchId, string transacitonId, decimal creditRequestAmount)
    {
        var par = new CreditLimitPar(customerId, SessionInfoPOS.SessionData.FinancialYear, branchId, transacitonId, creditRequestAmount);
        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/IsCreditAllowed", val);
            return await MyCommand.ResponseToBool(response);
        }
            return await StaticPOSDA.IsCreditAllowed(conLocal, par);
    }
   

     static List<RestaurentHall>? _halls;
    public  async Task<List<RestaurentHall>> GetHallsAndTables(int branchId)
    {
        if (_halls == null)
        {
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Transaction/GetHallsAndTables/{branchId}");
                _halls= await MyCommand.ResponseToData<List<RestaurentHall>>(response);
            }
            else
            _halls = await StaticPOSDA.GetHallsAndTables(conLocal, branchId);
        }
        return _halls;
    }
   
    static List<SpanCommission>? _spanTypes;
    public  async Task<List<SpanCommission>> GetSpanTypes()
    {
        if (_spanTypes == null)
        {
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Transaction/GetSpanTypes");
                _spanTypes= await MyCommand.ResponseToData<List<SpanCommission>>(response) ?? [];
            }
            else
            _spanTypes = await StaticPOSDA.GetSpanTypes(conLocal);
        }
        return _spanTypes;
    }
   
    public async  Task<POSLock?> GetByCashierUserName(string userName)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetByCashierUserName/{userName}");
            return await MyCommand.ResponseToData<POSLock>(response);
        }
        return await StaticPOSDA.GetByCashierUserName(conLocal, userName);
    }
   
    public  async Task<NextIdResult?> GetNextIds(int voucherTypeId, int year, int branchId)
    {
        var param = new NextIdParam(year, branchId, voucherTypeId);
        if (useApi)
        {
            var json = JsonSerializer.Serialize(param);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/GetNextIds", val);
            return await MyCommand.ResponseToData<NextIdResult>(response);
        }
        else return await StaticPOSDA.GetNextIds(conLocal, param);
    }

    public  async Task<SalesDetail?> GetSalesByLogin()
    {
        int loginId = SessionInfoPOS.SessionData.UserID;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetSalesByLogin/{loginId}");
            return await MyCommand.ResponseToData<SalesDetail>(response);
        }
        else return await StaticPOSDA.GetSalesByLogin(conLocal, loginId);
    }
   
    public  async Task<SalesCustomer?> GetSalesCustomerByMobileNo(string mobileNo)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetSalesCustomerByMobileNo/{mobileNo}");
            return await MyCommand.ResponseToData<SalesCustomer>(response);
        }
        return await StaticPOSDA.GetSalesCustomerByMobileNo(conLocal, mobileNo);
    }
   
    public  async Task<TransactionMain?> SelectTransactionByID(string id, bool isTemp, bool isReturn)
    {
        var par =new TransactioinByIdPar(id, isTemp, isReturn);
        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/SelectTransactionByID", val);
            return await MyCommand.ResponseToData<TransactionMain>(response);

        }

       return await StaticPOSDA.SelectTransactionByID(conLocal, par);
    }
   
    public  async Task<string> SaveTempTransaction(TransactionMain transactionMain, int updateMode)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(transactionMain);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/SaveTransactionTemp", val);
            return await MyCommand.ResponseToString(response);
        }
       return await StaticPOSDA.SaveTransactionTemp(conLocal, transactionMain);
    }
    
     static List<PaymentType>? _paymentTypes;
    public  async Task<List<PaymentType>> GetPaymentTypes(int branchId,string language)
    {
        if (_paymentTypes == null)
        {
            if (useApi)
            {
                var response = await httpClient.GetAsync($"Transaction/GetPaymentTypes/{branchId}/{language}");
                _paymentTypes= await MyCommand.ResponseToData<List<PaymentType>>(response) ?? [];
            }
            else
                _paymentTypes = await StaticPOSDA.GetPaymentTypes(conLocal, branchId, language);
        }
        return _paymentTypes;
    }
   
     async Task<TransactionMain> GetTrnObject(TransactionMain trans)
    {
        if (trans == null) return null;
        trans.Payments = new List<TransactionPayment>();
        var ptypes = await GetPaymentTypes(trans.BranchID,"Arabic");
        foreach (var item in ptypes)
        {
            trans.Payments.Add(new TransactionPayment
            {
                AccountNo = item.AccountNo,
                PaymentTypeId = item.id,
            });
        }

        foreach (var item in trans.Payments)
        {
            if (item.PaymentTypeId == 2)
            {
                item.AccountNo = "";
                item.Amount = trans.CashAmount;
                item.id = item.id;
            }
            if (item.PaymentTypeId == 3)
            {
                item.AccountNo = trans.BankAccountNo ?? "1-2-2-1";
                item.Amount = trans.CashAmount2;
                item.id = item.id;
            }
            if (item.PaymentTypeId == 4)
            {
                item.AccountNo = trans.CustomerID;
                item.Amount = trans.CreditAmount;
                item.id = item.id;
            }
        }
        trans.TransactionTime = DateTime.Now;
        return trans;
    }
    public async Task<string> SaveTransaction(TransactionMain transactionMain, int updateMode)
    {
        var t = ObjectCopier.Clone(transactionMain);
        var tra = await GetTrnObject(t);
        if (useApi)
        {
            var json = JsonSerializer.Serialize(tra);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/SaveTransaction", val);
            return await MyCommand.ResponseToString(response);

        }
        return await StaticPOSDA.SaveTransaction(conLocal, tra);
    }
   
    public  async Task<bool> ItemExists(string barcode)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/ItemExists/{barcode}");
            return await MyCommand.ResponseToBool(response);
        }
        return await StaticPOSDA.ItemExists(conLocal, barcode);
    }
   
    public  async Task<PosItemDetail?> UnitDetailSingleByBarcode(string barcode)
    {
        var data = SessionInfoPOS.SessionData;
        var param = new PosItemPar(data.FinancialYear, data.BranchID, barcode);
        if (useApi)
        {
            var json = JsonSerializer.Serialize(param);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/GetPosSingleItem", val);
            return await MyCommand.ResponseToData<PosItemDetail>(response);
        }
        else return await StaticPOSDA.UnitDetailSingleByBarcode(conLocal, param);
    }
   
    public  async Task<List<PosItemDetail>> UnitDetailByBarcode(string barcode)
    {
        var data = SessionInfoPOS.SessionData;
        var param = new PosItemPar(data.FinancialYear, data.BranchID, barcode);
        if (useApi)
        {
            var json = JsonSerializer.Serialize(param);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/GetPosMultipleItem", val);
            return await MyCommand.ResponseToData<List<PosItemDetail>>(response) ?? [];
        }
        else return await StaticPOSDA.UnitDetailByBarcode(conLocal, param);
    }

    public async Task<List<BarcodeSearchResult>> SearchByBarcode(string barcode)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/SearchByBarcode/{barcode}");
            return await MyCommand.ResponseToData<List<BarcodeSearchResult>>(response) ?? [];
        }
        return await StaticPOSDA.SearchByBarcode(conLocal, barcode);
    }
    
    public  async Task UpdateClearScreen(TransactionMain transactionMain)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(transactionMain);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/UpdateClearScreen", val);
            await MyCommand.ValidateResponse(response);
        }
        else
        await StaticPOSDA.UpdateClearScreen(conLocal, transactionMain);
    }
   
    public  async Task UpdateCancelledItem(int branchId, string userName, int voucherTypeId, int cancelType, string itemId, decimal qty, decimal numOfPieces, decimal total)
    {
        var par=new ItemDeletePar(branchId, userName, voucherTypeId, cancelType, itemId, qty, numOfPieces, total);
        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/UpdateCancelledItem", val);
            await MyCommand.ValidateResponse(response);
        }
        else
        await StaticPOSDA.UpdateCancelledItem(conLocal,par);
    }
    
    public async  Task<List<SalesCustomer>> GetPosCustomers()
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetPosCustomers");
            return await MyCommand.ResponseToData<List<SalesCustomer>>(response) ?? [];
        }
        return await StaticPOSDA.GetPosCustomers(conLocal);
    }
   
    public async  Task<int> UpdatePosCustomer(SalesCustomer customer)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(customer);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/UpdatePosCustomer", val);
            return await MyCommand.ResponseToInt(response);
        }
        return await StaticPOSDA.UpdatePosCustomer(conLocal, customer);
    }

    public  async Task<DataTable?> CustomersSalesReport(int customerId, DateTime from, DateTime to)
    {
        var par=new CustomerReportPar(customerId, from, to);
        DataTableJson? data = null;
        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/CustomersSalesReport", val);
            data= await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else 
            data = await StaticPOSDA.CustomersSalesReport(conLocal,par);

        var ser = new Serializer();
        if (data?.JsonData != null)
            return ser.DeserializeJson<SerializableDataTable>(data.JsonData).ToDataTable();
        return null;
    }
   
    public async Task<List<TempSearchResult>> SearchTemp(int branchId, int voucherTypeId, string userName)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/SearchTemp/{branchId}/{voucherTypeId}/{userName}");
            return await MyCommand.ResponseToData<List<TempSearchResult>>(response) ?? [];
        }
        return await StaticPOSDA.SearchTemp(conLocal, branchId, voucherTypeId, userName);
    }
   
    public  async Task<List<RestaurantItem>> GetChildListByRawMaterial(string rawMaterialID)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetChildListByRawMaterial/{rawMaterialID}");
            return await MyCommand.ResponseToData<List<RestaurantItem>>(response) ?? [];
        }
        return await StaticPOSDA.GetChildListByRawMaterial(conLocal, rawMaterialID);
    }
   
    public static  List<RestaurantItem> _childList = new List<RestaurantItem>();
    public  async Task LoadMenuItems(int BranchId, OnlineProvider provder = null)
    {
        if (provder == null) provder = new OnlineProvider { id = 1, name = "Cash" };
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/LoadMenuItems/{BranchId}/{provder.id}");
            _childList= await MyCommand.ResponseToData<List<RestaurantItem>>(response) ?? [];
        }
        else _childList= await StaticPOSDA.LoadMenuItems(conLocal,BranchId, provder.id) ?? [];
    }
    public  List<RestaurantItem> GetChilds(string parentId)
    {
        return _childList.FindAll(stock => stock.RefNo == parentId);
    }
    public  async Task<List<OnlineProvider>> GetProviders()
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync("Transaction/GetProviders");
            return await MyCommand.ResponseToData<List<OnlineProvider>>(response) ?? [];
        }
        return await StaticPOSDA.GetProviders(conLocal);
    }
    public async  Task<ItemCard?> SelectByItemID(string id)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetStockById/{id}");
            return await MyCommand.ResponseToData<ItemCard>(response);
        }
        return await StaticPOSDA.GetStockById(conLocal, id);
    }
    public async Task<string?> SaveCategory(ItemCard stock)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(stock);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/SaveCategory", val);
            return await MyCommand.ResponseToData<string>(response);
        }
        else return await StaticPOSDA.SaveCategory(conLocal, stock);
    }
    public  async Task<ItemCard?> SaveStock(ItemCard stock)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(stock);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/SaveStock", val);
            return await MyCommand.ResponseToData<ItemCard>(response);
        }
        else return await StaticPOSDA.SaveStock(conLocal, stock);
    }
   
    public  async Task<DataTable?> GetPosUsers()
    {
        DataTableJson? data = null;
        if (useApi)
        {
            var response = await httpClient.GetAsync("Transaction/GetPosUsers");
            data= await MyCommand.ResponseToData<DataTableJson>(response);
        }
        else 
            data = await StaticPOSDA.GetPosUsers(conLocal);

        var ser = new Serializer();
        if (data?.JsonData != null)
            return ser.DeserializeJson<SerializableDataTable>(data.JsonData).ToDataTable();
        return null;
    }
    public async  Task<List<PosSalesClosingDetail>> OutstandingsByUser(string userName)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/OutstandingsByUser/{userName}");
            return await MyCommand.ResponseToData<List<PosSalesClosingDetail>>(response) ?? [];
        }
        return await StaticPOSDA.OutstandingsByUser(conLocal, userName) ?? [];
    }
    public  async Task<int> SaveClosing(PosSalesClosingMaster closing)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(closing);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/SavePosClosing", val);
            return await MyCommand.ResponseToInt(response);
        }
        return await StaticPOSDA.SavePosClosing(conLocal, closing);
    }
   
    public async  Task<List<PosCategory>> GetPosCategories()
    {
        var userName = SessionInfoPOS.SessionData.UserName;

        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetPosCategories/{userName}");
            return await MyCommand.ResponseToData<List<PosCategory>>(response) ?? [];
        }
        return await StaticPOSDA.GetPosCategories(conLocal, userName);
    }
   
    public  async Task UpdateSpanResponse(SpanResponseData data)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(data);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/UpdateSpanResponse", val);
            await MyCommand.ValidateResponse(response);
        }
        else
            await StaticPOSDA.UpdateSpanResponse(conLocal, data);
    }
   
    public  async Task<(string?, string?)> GetLastIds()
    {
        var branchId = SessionInfoPOS.SessionData.BranchID;
        var userName = SessionInfoPOS.SessionData.UserName;
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/GetLastIds/{branchId}/{userName}");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"{error} is {response.StatusCode}");
            }
            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<(string, string)>(json, SessionInfoPOS.options);
        }
       return await StaticPOSDA.GetLastIds(conLocal, branchId, userName);
    }
   
     DataTable XreportCategoryToTable(List<XReportByCategoryModel> lst)
    {
        var table = new DataTable("SalesReport");
        table.Columns.Add("CategoryID", typeof(string));
        table.Columns.Add("CategoryName", typeof(string));
        table.Columns.Add("ItemID", typeof(string));
        table.Columns.Add("ItemName", typeof(string));
        table.Columns.Add("Quantity", typeof(decimal));
        table.Columns.Add("SalesPrice", typeof(decimal));

        lst.ForEach(x => table.Rows.Add(x.CategoryID, x.CategoryName, x.ItemID, x.ItemName, x.Quantity, x.SalesPrice));
        return table;
    }
    public  async Task<DataTable> SalesReportByCategory(int branchId, DateTime from, DateTime to, string categoryId, string trIdFrom, string trIdTo)
    {
        var param=new XReportByCategoryParam(branchId, from, to, categoryId, trIdFrom, trIdTo);
        List<XReportByCategoryModel> data= null;
        if (useApi)
        {
            var json = JsonSerializer.Serialize(param);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/XReportByCategory", val);
            data= await MyCommand.ResponseToData<List<XReportByCategoryModel>>(response) ?? [];
        }
        else 
            data = await StaticPOSDA.XReportByCategory(conLocal,param);
        return XreportCategoryToTable(data);
    }
    
    public  async Task<DataTable> XReport(DateTime from, DateTime to, string userName, int branchId, int reportType, string trIdFrom, string trIdTo, string language)
    {
        var param=new XReportParam(from, to, userName, branchId, reportType, trIdFrom, trIdTo, language);
        List<XReportModel> data = null;
        if (useApi)
        {
            var json = JsonSerializer.Serialize(param);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/XReport", val);
            data= await MyCommand.ResponseToData<List<XReportModel>>(response) ?? [];
        }
        else 
            data = await StaticPOSDA.XReport(conLocal,param);
        return XreportToTable(data);
    }
     DataTable XreportToTable(List<XReportModel> lst)
    {
        var table = new DataTable("DailyReport");
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("Col1", typeof(string));
        table.Columns.Add("Col2", typeof(string));
        table.Columns.Add("Col3", typeof(string));
        table.Columns.Add("Col4", typeof(decimal));
        table.Columns.Add("RowType", typeof(int));
        lst.ForEach(x => table.Rows.Add(x.id, x.Col1, x.Col2, x.Col3, x.Col4, x.RowType));
        return table;
    }
   
    public  async Task<DataTable> SearchInvoices(InvoiceSearchParam par)
    {
        List<InvoiceSearchResult> data = null;
        if (useApi)
        {
            var json = JsonSerializer.Serialize(par);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/SearchInvoices", val);
            data= await MyCommand.ResponseToData<List<InvoiceSearchResult>>(response) ?? [];
        }
        else
            data = await StaticPOSDA.SearchInvoices(conLocal, par);

        var table = new DataTable("Table1");
        table.Columns.Add("TransactionId", typeof(string));
        table.Columns.Add("TransactionDate", typeof(DateTime));
        table.Columns.Add("DailySerial", typeof(int));
        table.Columns.Add("NetAmount", typeof(decimal));
        data.ForEach(x => table.Rows.Add(x.TransactionId, x.TransactionDate, x.DailySerial, x.NetAmount));
        return table;
    }
    public async Task<SessionData?> UserLogin(LoginData login)
    {
        if (useApi)
        {
            var json = JsonSerializer.Serialize(login);
            var val = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("Transaction/Login", val);
            return await MyCommand.ResponseToData<SessionData>(response);
        }
        else
        {
            return await StaticPOSDA.UserLogin(conLocal, login);
        }
    }


    public  async Task LogOff(int loginId)
    {
        if (useApi)
        {
            var response = await httpClient.GetAsync($"Transaction/LogOff/{loginId}");
            await MyCommand.ValidateResponse(response);
        }
        else await StaticPOSDA.LogOff(conLocal, loginId);
    }
}
