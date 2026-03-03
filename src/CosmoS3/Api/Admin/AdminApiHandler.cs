namespace CosmoS3.Api.Admin
{
    
    using CosmoS3.Classes;
    using CosmoS3.Settings;
    using CosmoS3;
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Admin API handler.
    /// </summary>
    public class AdminApiHandler
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private SettingsBase _Settings;
        private S3Logger _Logging;
        private ConfigManager _Config;
        private BucketManager _Buckets;
        private AuthManager _Auth;

        private GetHandler _GetHandler;
        private PostHandler _PostHandler;
        private DeleteHandler _DeleteHandler;

        #endregion

        #region Constructors-and-Factories

        internal AdminApiHandler(
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

            _GetHandler = new GetHandler(_Settings, _Logging, _Config, _Buckets, _Auth);
            _PostHandler = new PostHandler(_Settings, _Logging, _Config, _Buckets, _Auth);
            _DeleteHandler = new DeleteHandler(_Settings, _Logging, _Config, _Buckets, _Auth);
        }

        #endregion

        #region Internal-Methods

        internal async Task Process(S3Context ctx)
        {
            switch (ctx.Http.Request.Method)
            {
                case CosmoApiServer.Core.Http.HttpMethod.GET:
                    await _GetHandler.Process(ctx);
                    return;
                case CosmoApiServer.Core.Http.HttpMethod.POST:
                    await _PostHandler.Process(ctx);
                    return;
                case CosmoApiServer.Core.Http.HttpMethod.DELETE:
                    await _DeleteHandler.Process(ctx);
                    return;
            }

            await ctx.Response.Send(CosmoS3.S3Objects.ErrorCode.InvalidRequest);
            return;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
