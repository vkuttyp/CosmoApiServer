namespace CosmoS3.Api.S3
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;
    using CosmoS3;
    using CosmoS3.Classes;

    internal static class ApiHelper
    {
        internal static RequestMetadata GetRequestMetadata(S3Context ctx)
        {
            if (ctx == null) return null;
            if (ctx.Metadata == null) return null;
            return (RequestMetadata)(ctx.Metadata);
        }

        internal static string AmazonTimestamp(DateTime dt)
        {
            return dt.ToString("yyyy-MM-ddTHH:mm:ss.fffz");
        }
    }
}