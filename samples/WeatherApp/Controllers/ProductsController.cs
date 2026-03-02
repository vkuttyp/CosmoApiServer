using CosmoSQLClient.Core;
using CosmoSQLClient.MsSql;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using WeatherApp.Extensions;
using WeatherApp.Models;

namespace WeatherApp.Controllers;

[Route("products")]
// [Authorize]
public class ProductsController(MsSqlConnectionPool pool) : ControllerBase
{
    // Each product streams to the client the instant its FOR JSON PATH chunk arrives
    [HttpGet]
    public IAsyncEnumerable<Product> GetAll() =>
        pool.QueryJsonStreamAsync<Product>(
            "SELECT ItemID as Id, ItemName as Name, SalesPrice as Price FROM Stock FOR JSON PATH");

    // With parameters
    [HttpGet("category/{id}")]
    public IAsyncEnumerable<Product> GetByCategory(int id) =>
        pool.QueryJsonStreamAsync<Product>(
            "SELECT Id, Name, Price FROM Products WHERE CategoryId = @p1 FOR JSON PATH",
            new SqlParameter(SqlValue.From(id), "p1"));

    // Transform/enrich each item before sending
    [HttpGet("enriched")]
    public async IAsyncEnumerable<ProductDto> GetEnriched()
    {
        await foreach (var product in pool.QueryJsonStreamAsync<Product>(
            "SELECT Id, Name, Price FROM Products FOR JSON PATH"))
        {
            yield return new ProductDto(product, discountedPrice: product.Price * 0.9m);
        }
    }
}
