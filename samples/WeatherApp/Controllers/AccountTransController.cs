using CosmoSQLClient.Core;
using CosmoSQLClient.MsSql;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using WeatherApp.Extensions;
using WeatherApp.Models;

namespace WeatherApp.Controllers;

[Route("account-trans")]
[Authorize]
public class AccountTransController(MsSqlConnectionPool pool) : ControllerBase
{
    // Stream all transactions — no buffering
    [HttpGet]
    public IAsyncEnumerable<AccountTrans> GetAll() =>
        pool.QueryJsonStreamAsync<AccountTrans>(
            "SELECT TOP 200 * FROM AccountTrans FOR JSON PATH");

    // Filter by account number
    [HttpGet("account/{accountNo}")]
    public IAsyncEnumerable<AccountTrans> GetByAccount(string accountNo) =>
        pool.QueryJsonStreamAsync<AccountTrans>(
            "SELECT * FROM AccountTrans WHERE AccountNo = @p1 FOR JSON PATH",
            new SqlParameter(SqlValue.From(accountNo), "p1"));

    // Enriched: stream with running total computed server-side
    [HttpGet("enriched")]
    public async IAsyncEnumerable<object> GetEnriched()
    {
        decimal runningTotal = 0;
        await foreach (var tx in pool.QueryJsonStreamAsync<AccountTrans>(
            "SELECT TOP 200 * FROM AccountTrans ORDER BY TransDate FOR JSON PATH"))
        {
            runningTotal += tx.Amount;
            yield return new
            {
                tx.Id,
                tx.TransDate,
                tx.Amount,
                tx.Description,
                RunningTotal = runningTotal
            };
        }
    }
}
