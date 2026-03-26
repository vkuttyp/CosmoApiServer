using System.Diagnostics;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using CosmoSQLClient.MsSql;
using BlazorSqlSample.Models;
using BlazorSqlSample.Components;
using Microsoft.Extensions.DependencyInjection;
using RenderFragment = Microsoft.AspNetCore.Components.RenderFragment;

namespace BlazorSqlSample.Controllers;

[Route("query")]
public class QueryController(MsSqlConnectionPool pool) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Index()
    {
        return RenderPage(new SqlQueryModel());
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromForm] string sql)
    {
        var model = new SqlQueryModel { Sql = sql };
        var sw = Stopwatch.StartNew();

        if (TryValidate(model))
        {
            try
            {
                await foreach (var row in pool.QueryStreamAsync(sql))
                {
                    if (model.Columns.Count == 0)
                    {
                        foreach (var col in row.Columns)
                            model.Columns.Add(col.Name);
                    }

                    var cells = new List<string?>(row.ColumnCount);
                    for (int i = 0; i < row.ColumnCount; i++)
                    {
                        cells.Add(row[i].IsNull ? null : row[i].ToString());
                    }
                    model.Rows.Add(cells);
                }
            }
            catch (Exception ex)
            {
                model.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                model.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            }
        }

        return await RenderPage(model);
    }

    private async Task<IActionResult> RenderPage(SqlQueryModel model)
    {
        var component = Query.Create(c => c.Model = model);
        component.HttpContext = HttpContext;
        
        // Transfer ModelState from controller to component
        foreach (var error in ModelState)
            component.ModelState[error.Key] = error.Value;
        
        // Use the same logic as ComponentScanner to wrap it
        if (ComponentScanner._appType != null)
        {
            var app = (CosmoApiServer.Core.Templates.ComponentBase)ActivatorUtilities.CreateInstance(HttpContext.RequestServices, ComponentScanner._appType);
            app.HttpContext = HttpContext;
            
            var childContentProp = ComponentScanner._appType.GetProperty("ChildContent");
            if (childContentProp != null)
            {
                var componentHtml = await component.RenderAsync();
                childContentProp.SetValue(app, (Microsoft.AspNetCore.Components.RenderFragment)(builder => 
                {
                    if (ComponentScanner._mainLayoutType != null)
                    {
                        builder.OpenComponent(0, ComponentScanner._mainLayoutType);
                        builder.AddComponentParameter(1, "Body", (Microsoft.AspNetCore.Components.RenderFragment)(bodyBuilder => 
                        {
                            bodyBuilder.AddMarkupContent(0, componentHtml);
                        }));
                        builder.CloseComponent();
                    }
                    else
                    {
                        builder.AddMarkupContent(0, componentHtml);
                    }
                }));
            }
            return View(app);
        }
        
        return View(component);
    }
}
