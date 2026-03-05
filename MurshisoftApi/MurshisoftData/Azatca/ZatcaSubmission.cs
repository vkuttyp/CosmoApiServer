using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Net;
using MurshisoftData.Models.POS;

namespace MurshisoftData.Azatca;

public class Result<T>
{
    public T Value { get; set; }
    public string Error { get; set; }
    public HttpStatusCode StatusCode { get; set; }
}
public class ZatcaSetting
{
    public int id { get; set; }
    public string ServerUrl { get; set; }
    public string S3Url { get; set; }
    public string S3ProxyUrl { get; set; }
    public List<ZatcaBranch> Branches { get; set; }
}

public class LoginInfo
{
    public string UserName { get; set; }
    public string Password { get; set; }

}
public class ZatcaTransaction
{
    public string TransactionId { get; set; }
}
public class ZatcaBranch
{
    public int BranchId { get; set; }
    public string UserName { get; set; }
    public string Password { get; set; }
    public string Jwt { get; set; }
}
public class ZatcaSubmission(string ConnectionString)
{
    static ZatcaSetting _zatcaSettings;
     DataAccess da = new DataAccess(ConnectionString);
    public  ZatcaSetting GetZatcaSettings()
    {
        if (_zatcaSettings == null)
        {
            _zatcaSettings = da.GetZatcaSettings();
        }
        return _zatcaSettings;
    }
    private static IHttpClientFactory httpFactory = new ServiceCollection()
       .AddHttpClient()
       .BuildServiceProvider()
       .GetRequiredService<IHttpClientFactory>();

    static HttpClient _httpLogin, _httpPost;
    static HttpClient HttpLogin
    {
        get
        {
            if (_httpLogin == null)
            {
                _httpLogin = HttpFactory.CreateClient();
            }
            return _httpLogin;
        }
    }
    static HttpClient HttpPost
    {
        get
        {
            if (_httpPost == null)
            {
                _httpPost = HttpFactory.CreateClient();
            }
            return _httpPost;
        }
    }

    public static IHttpClientFactory HttpFactory { get => HttpFactory1; set => HttpFactory1 = value; }
    public static IHttpClientFactory HttpFactory1 { get => httpFactory; set => httpFactory = value; }
    public  async Task<Result<string>> GetJwt(string userName, string password)
    {
        var r = new Result<string>();
        try
        {
            var response = await LoginResponse(userName, password);
            r.StatusCode = response.StatusCode;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                r.Value = await response.Content.ReadAsStringAsync();
            }
            else
            {
                r.Error = await response.Content?.ReadAsStringAsync() ?? response.ReasonPhrase;
            }
        }
        catch (Exception ex)
        {
            r.Error = ex.Message;
        }
        return r;
    }
     async Task<HttpResponseMessage> LoginResponse(string userName, string password)
    {
        var user = new LoginInfo { UserName = userName, Password = password };
        var json = JsonSerializer.Serialize(user);
        var payload = new StringContent(json, Encoding.UTF8, "application/json");
        string url = $"{GetZatcaSettings().ServerUrl}/User/Login";
        var response = await HttpLogin.PostAsync(url, payload);
        return response;
    }
    static JsonSerializerOptions options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private JsonSerializerOptions Options { get => options; set => options = value; }
    public  async Task SubmitRequest(string transactionId)
    {
        var branch = GetZatcaSettings().Branches.Where(a => a.BranchId == SessionInfoPOS.SessionData.BranchID).FirstOrDefault();

        HttpResponseMessage response = null;
        if (branch.Jwt == null)
        {
            var userName = branch.UserName;
            var password = branch.Password;
            var result = await GetJwt(userName, password);
            if (result.Value != null)
            {
                branch.Jwt = result.Value;
                response = await SubmitToBackground(transactionId, branch);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    branch.Jwt = null;
                    var msg = $"Login Error: Status Code: {response.StatusCode} \n" + response.ReasonPhrase;
                }

            }

        }
        else
        {
            response = await SubmitToBackground(transactionId, branch);
        }
        
    }
    private  async Task<HttpResponseMessage> SubmitToBackground(string transactionId, ZatcaBranch branch)
    {
        HttpPost.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", branch.Jwt);
        var tr = new ZatcaTransaction { TransactionId = transactionId };
        var json = JsonSerializer.Serialize(tr);
        var payload = new StringContent(json, Encoding.UTF8, "application/json");
        string url = $"{GetZatcaSettings().ServerUrl}/ZatcaInvoiceJob/SubmitInvoice";
        var response = await HttpPost.PostAsync(url, payload);
        return response;
    }

}

//public class testing
//{
//    public async Task test(string transactionId)
//    {
//      await  ZatcaSubmission.SubmitRequest(transactionId);
//    }
//}
