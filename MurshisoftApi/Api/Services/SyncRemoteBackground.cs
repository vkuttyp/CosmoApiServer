using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MurshisoftData;
using MurshisoftData.Models;
using System.Threading.Channels;

namespace Api.Services;

public class SyncRemoteBackground(Channel<SyncTransJob> channelTrans, Channel<SyncStockJob> channelStock, IServiceScopeFactory scopeFactory, ILogger<SyncRemoteBackground> logger):BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlServerDb>();

        _ = Task.Run(async () =>
        {
            await TransSync(db, stoppingToken);
        });
        _ = Task.Run(async () =>
        {
            await StockSync(db, stoppingToken);
        });
        return Task.CompletedTask;
    }
    async Task TransSync(SqlServerDb db, CancellationToken stoppingToken)
    {
        await foreach (var message in channelTrans.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var pending = await db.TransSyncGetPending(message.TransactionId, stoppingToken);
                if (pending != null)
                {
                    foreach (var transaction in pending.OrderBy(a => a.SerialNo))
                    {
                        var transactionId = transaction.TransactionID;
                        try
                        {
                            await db.SaveTransactionToRemote(transaction, stoppingToken);
                            logger.LogInformation("Successfully synced to remote: {transactionId}", transactionId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Updating remote failed: {transactionId}", transactionId);
                            break; // Exit loop on any processing exception
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in background job");
                break; // Exit loop on any processing exception
            }
        }
    }
    async Task StockSync(SqlServerDb db, CancellationToken   stoppingToken)
    {
        await foreach (var message in channelStock.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var pending = await db.StockSyncGetPending(message.ItemId, stoppingToken);
                if (pending != null)
                {
                    foreach (var item in pending.OrderBy(a => a.EntryDate))
                    {
                        try
                        {
                            await db.SaveStockToRemote(item, stoppingToken);
                            logger.LogInformation("Successfully synced item to remote: {ItemId}", item.ItemID);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Updating remote item failed: {ItemId}", item.ItemID);
                            break; // Exit loop on any processing exception
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Update Stock background job");
                break; // Exit loop on any processing exception
            }
        }
    }
}
