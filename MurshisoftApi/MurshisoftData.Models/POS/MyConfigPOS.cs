using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace MurshisoftData.Models.POS;
public class MyConfigurationsPOS
{
    const string configPath = "MyConfig.xml";
    public string SqlServerName { get; set; }
    public string DatabaseName { get; set; }
    public string UserName { get; set; }
    public string UserPassword { get; set; }
    public string IntegratedSecurity { get; set; }

    public static MyConfigurationsPOS ReadConfigurations()
    {
        MyConfigurationsPOS config = new MyConfigurationsPOS();
        try
        {
            using (XmlReader reader = XmlReader.Create(configPath))
            {

                reader.Read();
                reader.ReadStartElement("connectionstring");
                reader.ReadStartElement("servername");
                config.SqlServerName = reader.ReadString();
                reader.ReadEndElement();
                reader.ReadStartElement("databasename");
                config.DatabaseName = reader.ReadString();
                reader.ReadEndElement();

                reader.ReadStartElement("username");
                config.UserName = reader.ReadString();
                reader.ReadEndElement();
                reader.ReadStartElement("userpassword");
                config.UserPassword = reader.ReadString();
                reader.ReadEndElement();

                reader.ReadStartElement("integratedsecurity");
                config.IntegratedSecurity = reader.ReadString();
                reader.ReadEndElement();
                //SetConnectionString(config);
            }
        }
        catch
        {
            config.DatabaseName = "MurshiDb";
            config.IntegratedSecurity = "true";
            config.SqlServerName = ".";
            config.UserName = "sa";
            config.UserPassword = "aBCD111";
            //SetConnectionString(config);

            Save(config);
        }

        return config;
    }
    //private static void SetConnectionString(MyConfigurations config)
    //{
    //    SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
    //    builder.DataSource = config.SqlServerName;
    //    builder.UserID = config.UserName;
    //    builder.Password = config.UserPassword;
    //    builder.IntegratedSecurity = bool.Parse(config.IntegratedSecurity);
    //    builder.InitialCatalog = config.DatabaseName;
    //    builder.ApplicationName = "MurshisoftPOS";
    //    //builder.ConnectTimeout = 25;
    //    MySettings.ConnectionString = builder.ConnectionString;

    //}
    public static void Save(MyConfigurationsPOS config)
    {
        XmlWriterSettings settings = new XmlWriterSettings();
        //settings.OmitXmlDeclaration = true;
        settings.Indent = true;
        settings.Encoding = Encoding.UTF8;
        settings.ConformanceLevel = ConformanceLevel.Document;
        using (XmlWriter writer = XmlWriter.Create(configPath, settings))
        {
            string userName, password;
            userName = config.UserName == "" ? "sa" : config.UserName;
            password = config.UserPassword == "" ? "aBCD111" : config.UserPassword;
            // Write XML data.
            writer.WriteStartElement("connectionstring");
            writer.WriteElementString("servername", config.SqlServerName);
            writer.WriteElementString("databasename", config.DatabaseName);
            writer.WriteElementString("username", userName);
            writer.WriteElementString("userpassword", password);
            writer.WriteElementString("integratedsecurity", config.IntegratedSecurity);
            writer.WriteEndElement();
            writer.Flush();
        }

    }
}