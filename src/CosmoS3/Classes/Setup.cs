using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;


using CosmoS3.Settings;
using CosmoS3.Storage;
using CosmoS3;

using CosmoS3.Logging;
namespace CosmoS3.Classes
{
    /// <summary>
    /// Setup workflow.
    /// </summary>
    internal class Setup
    {
        #region Public-Members

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal Setup()
        {
            RunSetup();
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        private void RunSetup()
        {
            #region Variables

            DateTime timestamp = DateTime.Now;
            SettingsBase settings = new SettingsBase();

            #endregion

            #region Welcome

            Console.WriteLine("");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(Environment.NewLine +
                @"   _           ____  " + Environment.NewLine +
                @"  | |___ _____|__ /  " + Environment.NewLine +
                @"  | / -_|_-<_-<|_ \  " + Environment.NewLine +
                @"  |_\___/__/__/___/  " + Environment.NewLine +
                @"                     " + Environment.NewLine +
                Environment.NewLine);

            Console.ResetColor();

            Console.WriteLine("");
            Console.WriteLine("<3 :: Less3 :: S3-Compatible Object Storage");
            Console.WriteLine("");
            //                          1         2         3         4         5         6         7
            //                 12345678901234567890123456789012345678901234567890123456789012345678901234567890
            Console.WriteLine("Thank you for using Less3!  We're putting together a basic system configuration");
            Console.WriteLine("so you can be up and running quickly.  You'll want to modify the system.json");
            Console.WriteLine("file after to ensure a more secure operating environment.");
            Console.WriteLine("");

            #endregion

            #region Temporary-Instances

            S3Logger logging = new S3Logger("127.0.0.1");

            #endregion

            #region Database-and-ORM

            //                          1         2         3         4         5         6         7
            //                 12345678901234567890123456789012345678901234567890123456789012345678901234567890
            Console.WriteLine("");
            Console.WriteLine("Less3 requires access to a database and supports Sqlite, Microsoft SQL Server,");
            Console.WriteLine("MySQL, and PostgreSQL.  Please provide access details for your database.  The");
            Console.WriteLine("user account supplied must have the ability to CREATE and DROP tables along");
            Console.WriteLine("with issue queries containing SELECT, INSERT, UPDATE, and DELETE.  Setup will");
            Console.WriteLine("attempt to create tables on your behalf if they dont exist.");
            Console.WriteLine("");

            bool dbSet = false;

            while (!dbSet)
            {
                string userInput = "sqlserver";
                switch (userInput)
                {
                   
                    case "sqlserver":
                        settings.Database = new DatabaseSettings(
                            "localhost",
                            1433,
                            "sa",
                            null,
                            null,
                            "MurshiDb"
                            );
                        dbSet = true;
                        break;
                   
                        dbSet = true;
                        break;
                }
            }

            try
            {
                File.WriteAllText("system.json", SerializationHelper.SerializeJson(settings, true));
            }
            catch
            {
                Console.WriteLine("Unable to write system.json");
                Environment.Exit(-1);
                return;
            }

            if (!Directory.Exists(settings.Storage.DiskDirectory))
                Directory.CreateDirectory(settings.Storage.DiskDirectory);

            if (!Directory.Exists(settings.Storage.TempDirectory))
                Directory.CreateDirectory(settings.Storage.TempDirectory);

            #endregion

            #region Create-Configuration-Database

           
            ConfigManager config = new ConfigManager(settings, logging);

            string userGuid = "default";
            config.AddUser(new User(userGuid, "Default user", "default@default.com"));
            config.AddCredential(userGuid, "My first access key", "default", "default", false);

            Bucket bucketConfig = new Bucket(
                "default",
                userGuid,
                userGuid,
                StorageDriverType.Disk,
                settings.Storage.DiskDirectory + "default/Objects/");
            bucketConfig.EnablePublicRead = true;
            bucketConfig.EnablePublicWrite = false;
            bucketConfig.EnableVersioning = false;

            config.AddBucket(bucketConfig);

            #endregion

            #region Write-Sample-Objects

            BucketClient bucket = new BucketClient(settings, logging, bucketConfig);

            DateTime ts = DateTime.Now.ToUniversalTime();

            string htmlFile = SampleHtmlFile("http://github.com/jchristn/less3");
            string textFile = SampleTextFile("http://github.com/jchristn/less3");
            string jsonFile = SampleJsonFile("http://github.com/jchristn/less3");

            Obj obj1 = new Obj();
            obj1.OwnerGUID = "default";
            obj1.AuthorGUID = "default";
            obj1.BlobFilename = Guid.NewGuid().ToString();
            obj1.ContentLength = htmlFile.Length;
            obj1.ContentType = "text/html";
            obj1.Key = "hello.html";
            obj1.Md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(htmlFile))).ToLowerInvariant();
            obj1.Version = 1;
            obj1.IsFolder = false;
            obj1.DeleteMarker = false;
            obj1.CreatedUtc = ts;
            obj1.LastUpdateUtc = ts;
            obj1.LastAccessUtc = ts;

            Obj obj2 = new Obj();
            obj2.OwnerGUID = "default";
            obj2.AuthorGUID = "default";
            obj2.BlobFilename = Guid.NewGuid().ToString();
            obj2.ContentLength = htmlFile.Length;
            obj2.ContentType = "text/plain";
            obj2.Key = "hello.txt";
            obj2.Md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(textFile))).ToLowerInvariant();
            obj2.Version = 1;
            obj2.IsFolder = false;
            obj2.DeleteMarker = false;
            obj2.CreatedUtc = ts;
            obj2.LastUpdateUtc = ts;
            obj2.LastAccessUtc = ts;

            Obj obj3 = new Obj();
            obj3.OwnerGUID = "default";
            obj3.AuthorGUID = "default";
            obj3.BlobFilename = Guid.NewGuid().ToString();
            obj3.ContentLength = htmlFile.Length;
            obj3.ContentType = "application/json";
            obj3.Key = "hello.json";
            obj3.Md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(jsonFile))).ToLowerInvariant();
            obj3.Version = 1;
            obj3.IsFolder = false;
            obj3.DeleteMarker = false;
            obj3.CreatedUtc = ts;
            obj3.LastUpdateUtc = ts;
            obj3.LastAccessUtc = ts;

            bucket.AddObject(obj1, Encoding.UTF8.GetBytes(htmlFile));
            bucket.AddObject(obj2, Encoding.UTF8.GetBytes(textFile));
            bucket.AddObject(obj3, Encoding.UTF8.GetBytes(jsonFile));

            File.WriteAllBytes("./system.json", Encoding.UTF8.GetBytes(SerializationHelper.SerializeJson(settings, true)));

            #endregion 

            #region Wrap-Up

            //                          1         2         3         4         5         6         7
            //                 12345678901234567890123456789012345678901234567890123456789012345678901234567890 
            Console.WriteLine("");
            Console.WriteLine("All finished!");
            Console.WriteLine("");
            Console.WriteLine("If you ever want to return to this setup wizard, just re-run the application");
            Console.WriteLine("from the terminal with the 'setup' argument.");
            Console.WriteLine("");
            Console.WriteLine("We created a bucket containing a few sample files for you so that you can see");
            Console.WriteLine("your node in action.  Access these files in the 'default' bucket using the");
            Console.WriteLine("AWS SDK or your favorite S3 browser tool.");
            Console.WriteLine("");
            Console.WriteLine("  http://localhost:8000/default/hello.html");
            Console.WriteLine("  http://localhost:8000/default/hello.txt");
            Console.WriteLine("  http://localhost:8000/default/hello.json");
            Console.WriteLine("");
            Console.WriteLine("  Access key  : default");
            Console.WriteLine("  Secret key  : default");
            Console.WriteLine("  Bucket name : default (public read enabled!)");
            Console.WriteLine("  S3 endpoint : http://localhost:8000");
            Console.WriteLine("");
            Console.WriteLine("IMPORTANT: be sure to supply a hostname in the system.json Webserver.Hostname");
            Console.WriteLine("property if you wish to allow access from other machines.  Your node is currently");
            Console.WriteLine("only accessible via localhost.  Do not use an IP address for this value.");
            Console.WriteLine("");

            #endregion
        }

        private string SampleHtmlFile(string link)
        {
            string html =
                "<html>" + Environment.NewLine +
                "   <head>" + Environment.NewLine +
                "      <title>&lt;3 :: Less3 :: S3-Compatible Object Storage</title>" + Environment.NewLine +
                "      <style>" + Environment.NewLine +
                "          body {" + Environment.NewLine +
                "            font-family: arial;" + Environment.NewLine +
                "          }" + Environment.NewLine +
                "          pre {" + Environment.NewLine +
                "            background-color: #e5e7ea;" + Environment.NewLine +
                "            color: #333333; " + Environment.NewLine +
                "          }" + Environment.NewLine +
                "          h3 {" + Environment.NewLine +
                "            color: #333333; " + Environment.NewLine +
                "            padding: 4px;" + Environment.NewLine +
                "            border: 4px;" + Environment.NewLine +
                "          }" + Environment.NewLine +
                "          p {" + Environment.NewLine +
                "            color: #333333; " + Environment.NewLine +
                "            padding: 4px;" + Environment.NewLine +
                "            border: 4px;" + Environment.NewLine +
                "          }" + Environment.NewLine +
                "          a {" + Environment.NewLine +
                "            background-color: #4cc468;" + Environment.NewLine +
                "            color: white;" + Environment.NewLine +
                "            padding: 4px;" + Environment.NewLine +
                "            border: 4px;" + Environment.NewLine +
                "         text-decoration: none; " + Environment.NewLine +
                "          }" + Environment.NewLine +
                "          li {" + Environment.NewLine +
                "            padding: 6px;" + Environment.NewLine +
                "            border: 6px;" + Environment.NewLine +
                "          }" + Environment.NewLine +
                "      </style>" + Environment.NewLine +
                 "   </head>" + Environment.NewLine +
                "   <body>" + Environment.NewLine +
                "      <pre>" + Environment.NewLine +
                WebUtility.HtmlEncode(Constants.Logo) +
                "      </pre>" + Environment.NewLine +
                "      <p>Congratulations, your Less3 node is running!</p>" + Environment.NewLine +
                "      <p>" + Environment.NewLine +
                "        <a href='" + link + "' target='_blank'>Source Code</a>" + Environment.NewLine +
                "      </p>" + Environment.NewLine +
                "   </body>" + Environment.NewLine +
                "</html>";

            return html;
        }

        private string SampleJsonFile(string link)
        {
            Dictionary<string, object> ret = new Dictionary<string, object>();
            ret.Add("Title", "Welcome to Less3");
            ret.Add("Body", "If you can see this file, your Less3 node is running!");
            ret.Add("Github", link);
            return SerializationHelper.SerializeJson(ret, true);
        }

        private string SampleTextFile(string link)
        {
            string text =
                "Welcome to Less3!" + Environment.NewLine + Environment.NewLine +
                "If you can see this file, your Less3 node is running!  Now try " +
                "accessing this same URL in your browser, but use the .html extension!" + Environment.NewLine + Environment.NewLine +
                "Find us on Github here: " + link + Environment.NewLine + Environment.NewLine;

            return text;
        }

        #endregion
    }
}