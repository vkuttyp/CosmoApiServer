namespace CosmoS3.Api.Admin
{
    
    using CosmoS3.Classes;
    using CosmoS3.Settings;
    using CosmoS3;
    using System;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Admin API POST handler.
    /// </summary>
    internal class PostHandler
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private SettingsBase _Settings;
        private S3Logger _Logging;
        private ConfigManager _Config;
        private BucketManager _Buckets;
        private AuthManager _Auth;

        #endregion

        #region Constructors-and-Factories

        internal PostHandler(
            SettingsBase settings,
            S3Logger logging,
            ConfigManager config,
            BucketManager buckets,
            AuthManager auth)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (buckets == null) throw new ArgumentNullException(nameof(buckets));
            if (auth == null) throw new ArgumentNullException(nameof(auth));

            _Settings = settings;
            _Logging = logging;
            _Config = config;
            _Buckets = buckets;
            _Auth = auth;
        }

        #endregion

        #region Internal-Methods

        internal async Task Process(S3Context ctx)
        {
            if (ctx.Http.Request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)[1].Equals("buckets"))
            {
                await PostBuckets(ctx);
                return;
            }
            else if (ctx.Http.Request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)[1].Equals("users"))
            {
                await PostUsers(ctx);
                return;
            }
            else if (ctx.Http.Request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)[1].Equals("credentials"))
            {
                await PostCredentials(ctx);
                return;
            }

            await ctx.Response.Send(CosmoS3.S3Objects.ErrorCode.InvalidRequest);
        }

        #endregion

        #region Private-Methods

        private async Task PostBuckets(S3Context ctx)
        {
            if (ctx.Http.Request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2)
            {
                await ctx.Response.Send(CosmoS3.S3Objects.ErrorCode.InvalidRequest);
                return;
            }

            byte[] data = null;
            Bucket bucket = null;

            try
            {
                data = ctx.Http.Request.Body;
                bucket = SerializationHelper.DeserializeJson<Bucket>(Encoding.UTF8.GetString(data));
            }
            catch (Exception)
            {
                await ctx.Response.Send(CosmoS3.S3Objects.ErrorCode.InvalidRequest);
                return;
            }

            Bucket tempBucket = _Config.GetBucketByName(bucket.Name);
            if (tempBucket != null)
            {
                await ctx.Response.Send(CosmoS3.S3Objects.ErrorCode.BucketAlreadyExists);
                return;
            }

            _Buckets.Add(bucket);

            ctx.Response.StatusCode = 201;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send();
        }

        private async Task PostUsers(S3Context ctx)
        {
            if (ctx.Http.Request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2)
            {
                await ctx.Response.Send(CosmoS3.S3Objects.ErrorCode.InvalidRequest);
                return;
            }

            User user = null;

            try
            {
                user = SerializationHelper.DeserializeJson<User>(ctx.Request.DataAsString);
            }
            catch (Exception)
            {
                await ctx.Response.Send(CosmoS3.S3Objects.ErrorCode.InvalidRequest);
                return;
            }

            User tempUser = _Config.GetUserByEmail(user.Email);
            if (tempUser != null)
            {
                ctx.Response.StatusCode = 409;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.Send();
                return;
            }

            tempUser = _Config.GetUserByGuid(user.GUID);
            if (tempUser != null)
            {
                ctx.Response.StatusCode = 409;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.Send();
                return;
            }

            _Config.AddUser(user);

            ctx.Response.StatusCode = 201;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send();
        }

        private async Task PostCredentials(S3Context ctx)
        {
            if (ctx.Http.Request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2)
            {
                await ctx.Response.Send(CosmoS3.S3Objects.ErrorCode.InvalidRequest);
                return;
            }

            byte[] data = null;
            Credential cred = null;

            try
            {
                data = ctx.Http.Request.Body;
                cred = SerializationHelper.DeserializeJson<Credential>(Encoding.UTF8.GetString(data));
            }
            catch (Exception)
            {
                await ctx.Response.Send(CosmoS3.S3Objects.ErrorCode.InvalidRequest);
                return;
            }

            Credential tempCred = _Config.GetCredentialByAccessKey(cred.AccessKey);
            if (tempCred != null)
            {
                ctx.Response.StatusCode = 409;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.Send();
                return;
            }

            _Config.AddCredential(cred);

            ctx.Response.StatusCode = 201;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send();
        }

        #endregion
    }
}
