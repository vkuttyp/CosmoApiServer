namespace CosmoApiServer.Core.Http;

/// <summary>
/// Provides access to the current <see cref="HttpContext"/> from anywhere in the DI graph.
/// Registered as a scoped service via AddHttpContextAccessor().
/// </summary>
public interface IHttpContextAccessor
{
    HttpContext? HttpContext { get; set; }
}

public sealed class HttpContextAccessor : IHttpContextAccessor
{
    private static readonly AsyncLocal<HttpContextHolder> _current = new();

    public HttpContext? HttpContext
    {
        get => _current.Value?.Context;
        set
        {
            var holder = _current.Value;
            if (holder is not null)
                holder.Context = null; // clear previous holder to avoid leaking across async flows
            if (value is not null)
                _current.Value = new HttpContextHolder { Context = value };
        }
    }

    private sealed class HttpContextHolder
    {
        public HttpContext? Context;
    }
}
