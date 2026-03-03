namespace CosmoS3.Settings;

/// <summary>
/// CosmoS3 settings.
/// </summary>
public class SettingsBase
{
    /// <summary>Enable or disable signature validation.</summary>
    public bool ValidateSignatures { get; set; } = true;

    /// <summary>Base domain, if using virtual hosted-style URLs, e.g. "localhost".</summary>
    public string? BaseDomain { get; set; } = null;

    /// <summary>API key header for admin API requests.</summary>
    public string HeaderApiKey
    {
        get => _HeaderApiKey;
        set => _HeaderApiKey = (!string.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(HeaderApiKey)));
    }

    /// <summary>Admin API key.</summary>
    public string AdminApiKey
    {
        get => _AdminApiKey;
        set => _AdminApiKey = (!string.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(AdminApiKey)));
    }

    /// <summary>Region string.</summary>
    public string RegionString
    {
        get => _RegionString;
        set => _RegionString = (!string.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(RegionString)));
    }

    /// <summary>Database settings.</summary>
    public DatabaseSettings Database
    {
        get => _Database;
        set => _Database = value ?? throw new ArgumentNullException(nameof(Database));
    }

    /// <summary>Storage settings.</summary>
    public StorageSettings Storage
    {
        get => _Storage;
        set => _Storage = value ?? throw new ArgumentNullException(nameof(Storage));
    }

    /// <summary>Logging settings.</summary>
    public LoggingSettings Logging
    {
        get => _Logging;
        set => _Logging = value ?? throw new ArgumentNullException(nameof(Logging));
    }

    /// <summary>Debugging settings.</summary>
    public DebugSettings Debug
    {
        get => _Debug;
        set => _Debug = value ?? throw new ArgumentNullException(nameof(Debug));
    }

    /// <summary>
    /// Optional in-memory users. When populated, ConfigManager checks this list before
    /// querying the database — useful for development/testing without a SQL Server.
    /// </summary>
    public List<Classes.User> Users { get; set; } = new();

    /// <summary>
    /// Optional in-memory credentials paired with <see cref="Users"/>.
    /// Each entry must have <c>AccessKey</c>, <c>SecretKey</c>, and <c>UserGUID</c>
    /// matching a user in <see cref="Users"/>.
    /// </summary>
    public List<Classes.Credential> Credentials { get; set; } = new();

    /// <summary>
    /// Optional in-memory buckets. When <see cref="Users"/> is populated (no-DB mode),
    /// bucket queries read/write this list instead of the database.
    /// </summary>
    public List<Classes.Bucket> Buckets { get; set; } = new();

    /// <summary>True when running in no-DB mode (Users list is seeded).</summary>
    internal bool NoDatabase => Users.Count > 0;

    private string _HeaderApiKey = "x-api-key";
    private string _AdminApiKey = "cosmos3admin";
    private string _RegionString = "us-west-1";
    private DatabaseSettings _Database = new();
    private StorageSettings _Storage = new();
    private LoggingSettings _Logging = new();
    private DebugSettings _Debug = new();

    public SettingsBase() { }
}
