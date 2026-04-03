using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.ProblemDetails;

public interface IProblemDetailsService
{
    ValueTask WriteAsync(ProblemDetailsContext context);
}

public sealed class ProblemDetailsContext
{
    public required HttpContext HttpContext { get; init; }
    public ProblemDetails? ProblemDetails { get; init; }
    public Exception? Exception { get; init; }
}

public sealed class ProblemDetailsOptions
{
    /// <summary>Customise the ProblemDetails instance before it is serialised.</summary>
    public Action<ProblemDetailsContext>? CustomizeProblemDetails { get; set; }
}
