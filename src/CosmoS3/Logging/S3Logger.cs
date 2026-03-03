namespace CosmoS3.Logging;

/// <summary>
/// Simple logger to replace HelperLib.Logging.LoggingModule throughout CosmoS3.
/// </summary>
public sealed class S3Logger
{
    private readonly string _prefix;
    private readonly Action<string>? _output;
    private readonly LogLevel _minLevel;

    public S3Logger(string prefix = "", LogLevel minLevel = LogLevel.Info, Action<string>? output = null)
    {
        _prefix = prefix;
        _minLevel = minLevel;
        _output = output ?? Console.WriteLine;
    }

    public void Debug(string msg)
    {
        if (_minLevel <= LogLevel.Debug)
            Write("DEBUG", msg);
    }

    public void Info(string msg)
    {
        if (_minLevel <= LogLevel.Info)
            Write("INFO ", msg);
    }

    public void Warn(string msg)
    {
        if (_minLevel <= LogLevel.Warn)
            Write("WARN ", msg);
    }

    public void Exception(string method, Exception ex)
    {
        if (_minLevel <= LogLevel.Error)
            Write("ERROR", $"{method}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }

    public void Exception(Exception ex, string method, string msg)
        => Exception(method + " " + msg, ex);

    private void Write(string level, string msg)
    {
        _output!($"[{level}] {_prefix}{msg}");
    }
}

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    None = 99,
}
