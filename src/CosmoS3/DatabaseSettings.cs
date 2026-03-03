using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CosmoS3;

public class DatabaseSettings
{
    public string Hostname { get; set; } = null;
    public int Port
    {
        get
        {
            return _Port;
        }
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(Port));
            _Port = value;
        }
    }

    /// <summary>
    /// The username to use when accessing the database.
    /// </summary>
    public string Username { get; set; } = null;

    /// <summary>
    /// The password to use when accessing the database.
    /// </summary>
    public string Password { get; set; } = null;

    /// <summary>
    /// For SQL Server Express, the instance name.
    /// </summary>
    public string Instance { get; set; } = null;

    /// <summary>
    /// The name of the database.
    /// </summary>
    public string DatabaseName { get; set; } = null;

    /// <summary>
    /// Debug settings.
    /// </summary>
    public DebugSettings Debug
    {
        get
        {
            return _Debug;
        }
        set
        {
            if (value == null) throw new ArgumentNullException(nameof(Debug));
            _Debug = value;
        }
    }


    private int _Port = 0;
    private DebugSettings _Debug = new DebugSettings();

    public DatabaseSettings()
    {

    }

  

    /// <summary>
    /// Instantiate the object using SQL Server, MySQL, or PostgreSQL.
    /// </summary>
    /// <param name="dbType">The type of database.</param>
    /// <param name="hostname">The hostname of the database server.</param>
    /// <param name="port">The TCP port number on which the server is listening.</param>
    /// <param name="username">The username to use when accessing the database.</param>
    /// <param name="password">The password to use when accessing the database.</param> 
    /// <param name="dbName">The name of the database.</param>
    public DatabaseSettings(string dbType, string hostname, int port, string username, string password, string dbName)
    {
        if (String.IsNullOrEmpty(dbType)) throw new ArgumentNullException(nameof(dbType));
        if (String.IsNullOrEmpty(hostname)) throw new ArgumentNullException(nameof(hostname));
        if (String.IsNullOrEmpty(dbName)) throw new ArgumentNullException(nameof(dbName));


        Hostname = hostname;
        Port = port;
        Username = username;
        Password = password;
        Instance = null;
        DatabaseName = dbName;
    }

    /// <summary>
    /// Instantiate the object using SQL Server, MySQL, or PostgreSQL.
    /// </summary>
    /// <param name="dbType">The type of database.</param>
    /// <param name="hostname">The hostname of the database server.</param>
    /// <param name="port">The TCP port number on which the server is listening.</param>
    /// <param name="username">The username to use when accessing the database.</param>
    /// <param name="password">The password to use when accessing the database.</param> 
    /// <param name="dbName">The name of the database.</param>
    public DatabaseSettings(string hostname, int port, string username, string password, string dbName)
    {
        if (String.IsNullOrEmpty(hostname)) throw new ArgumentNullException(nameof(hostname));
        if (String.IsNullOrEmpty(dbName)) throw new ArgumentNullException(nameof(dbName));


        Hostname = hostname;
        Port = port;
        Username = username;
        Password = password;
        Instance = null;
        DatabaseName = dbName;
    }

    /// <summary>
    /// Instantiate the object for SQL Server Express.
    /// </summary> 
    /// <param name="hostname">The hostname of the database server.</param>
    /// <param name="port">The TCP port number on which the server is listening.</param>
    /// <param name="username">The username to use when accessing the database.</param>
    /// <param name="password">The password to use when accessing the database.</param>
    /// <param name="instance">For SQL Server Express, the instance name.</param>
    /// <param name="dbName">The name of the database.</param>
    public DatabaseSettings(string hostname, int port, string username, string password, string instance, string dbName)
    {
        if (String.IsNullOrEmpty(hostname)) throw new ArgumentNullException(nameof(hostname));
        if (String.IsNullOrEmpty(dbName)) throw new ArgumentNullException(nameof(dbName));

        Hostname = hostname;
        Port = port;
        Username = username;
        Password = password;
        Instance = instance;
        DatabaseName = dbName;
    }


    public class DebugSettings
    {
        /// <summary>
        /// Enable debugging for queries.
        /// </summary>
        public bool EnableForQueries { get; set; } = false;

        /// <summary>
        /// Enable debugging for results.
        /// </summary>
        public bool EnableForResults { get; set; } = false;

        /// <summary>
        /// Action to invoke when sending a debug message.
        /// </summary>
        public Action<string> Logger { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DebugSettings()
        {

        }
    }

}