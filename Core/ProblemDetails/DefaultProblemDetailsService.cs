using System.Text.Json;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.ProblemDetails;

public sealed class DefaultProblemDetailsService(ProblemDetailsOptions options) : IProblemDetailsService
{
    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var pd = context.ProblemDetails ?? new ProblemDetails();
        var status = pd.Status ?? context.HttpContext.Response.StatusCode;

        pd.Status ??= status;
        pd.Type ??= ProblemDetails.TypeForStatus(status);
        pd.Title ??= ProblemDetails.TitleForStatus(status);
        pd.Instance ??= context.HttpContext.Request.Path;

        options.CustomizeProblemDetails?.Invoke(context);

        context.HttpContext.Response.StatusCode = status;
        context.HttpContext.Response.Headers["Content-Type"] = "application/problem+json";
        context.HttpContext.Response.Write(JsonSerializer.SerializeToUtf8Bytes(pd));

        return ValueTask.CompletedTask;
    }
}
