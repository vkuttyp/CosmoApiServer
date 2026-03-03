using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;

namespace CosmoS3.Tests;

public class WebsiteTests : IClassFixture<S3Fixture>
{
    private readonly S3Fixture _fixture;
    // Use a non-redirecting HttpClient for 301 assertions
    private readonly HttpClient _noRedirectClient;

    public WebsiteTests(S3Fixture fixture)
    {
        _fixture = fixture;
        _noRedirectClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(fixture.EndpointUrl)
        };
    }

    private Task PutHtmlObject(string key, string body) =>
        _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            ContentBody = body,
            ContentType = "text/html"
        });

    private Task ConfigureWebsite(
        string indexSuffix = "index.html",
        string errorKey = "error.html",
        List<RoutingRule>? routingRules = null,
        RoutingRuleRedirect? redirectAll = null)
    {
        var config = new WebsiteConfiguration
        {
            IndexDocumentSuffix = indexSuffix,
            ErrorDocument = errorKey,
        };
        if (routingRules != null)
            config.RoutingRules = routingRules;
        if (redirectAll != null)
            config.RedirectAllRequestsTo = redirectAll;

        return _fixture.S3Client.PutBucketWebsiteAsync(new PutBucketWebsiteRequest
        {
            BucketName = _fixture.BucketName,
            WebsiteConfiguration = config
        });
    }

    [Fact]
    public async Task PutWebsiteConfig_Succeeds()
    {
        await PutHtmlObject("index.html", "<html>index</html>");
        await PutHtmlObject("error.html", "<html>error</html>");

        // Should not throw
        await ConfigureWebsite();
    }

    [Fact]
    public async Task GetWebsiteConfig_ReturnsConfig()
    {
        await PutHtmlObject("index.html", "<html>index</html>");
        await PutHtmlObject("error.html", "<html>error</html>");
        await ConfigureWebsite();

        var response = await _fixture.S3Client.GetBucketWebsiteAsync(new GetBucketWebsiteRequest
        {
            BucketName = _fixture.BucketName
        });

        Assert.Equal("index.html", response.WebsiteConfiguration.IndexDocumentSuffix);
    }

    [Fact]
    public async Task GetWebsiteConfig_NotConfigured_Returns404()
    {
        // Use a fresh bucket guaranteed to have no website config (short name to stay under 50 chars)
        var freshBucket = $"fwt-{Guid.NewGuid().ToString("N")[..8]}";
        await _fixture.S3Client.PutBucketAsync(freshBucket);
        try
        {
            Exception? caughtEx = null;
            GetBucketWebsiteResponse? response = null;
            try
            {
                response = await _fixture.S3Client.GetBucketWebsiteAsync(new GetBucketWebsiteRequest
                {
                    BucketName = freshBucket
                });
            }
            catch (Exception e)
            {
                caughtEx = e;
            }

            if (caughtEx is AmazonS3Exception s3ex)
                Assert.Equal("NoSuchWebsiteConfiguration", s3ex.ErrorCode);
            else if (caughtEx != null)
                throw new Exception($"Unexpected exception: {caughtEx.GetType().Name}: {caughtEx.Message}", caughtEx);
            else
                // AWSSDK returns response instead of throwing for custom endpoints
                Assert.Equal(HttpStatusCode.NotFound, response!.HttpStatusCode);
        }
        finally
        {
            await _fixture.S3Client.DeleteBucketAsync(freshBucket);
        }
    }

    [Fact]
    public async Task DeleteWebsiteConfig_Succeeds()
    {
        await PutHtmlObject("index.html", "<html>index</html>");
        await PutHtmlObject("error.html", "<html>error</html>");
        await ConfigureWebsite();

        await _fixture.S3Client.DeleteBucketWebsiteAsync(new DeleteBucketWebsiteRequest
        {
            BucketName = _fixture.BucketName
        });

        Exception? caughtEx2 = null;
        GetBucketWebsiteResponse? response2 = null;
        try
        {
            response2 = await _fixture.S3Client.GetBucketWebsiteAsync(new GetBucketWebsiteRequest
            {
                BucketName = _fixture.BucketName
            });
        }
        catch (Exception e) { caughtEx2 = e; }

        if (caughtEx2 is AmazonS3Exception s3ex2)
            Assert.Equal("NoSuchWebsiteConfiguration", s3ex2.ErrorCode);
        else if (caughtEx2 != null)
            throw new Exception($"Unexpected exception: {caughtEx2.GetType().Name}: {caughtEx2.Message}", caughtEx2);
        else
            // AWSSDK returns response instead of throwing for custom endpoints
            Assert.Equal(HttpStatusCode.NotFound, response2!.HttpStatusCode);
    }

    [Fact]
    public async Task StaticServing_IndexDocument_ServedOnBucketRoot()
    {
        const string htmlBody = "<html><body>Hello Index</body></html>";
        await PutHtmlObject("index.html", htmlBody);
        await PutHtmlObject("error.html", "<html>error</html>");
        await ConfigureWebsite();

        var response = await _fixture.HttpClient.GetAsync($"/{_fixture.BucketName}/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hello Index", body);
    }

    [Fact]
    public async Task StaticServing_ErrorDocument_ServedOn404()
    {
        const string errorBody = "<html><body>Custom Error Page</body></html>";
        await PutHtmlObject("index.html", "<html>index</html>");
        await PutHtmlObject("error.html", errorBody);
        await ConfigureWebsite();

        var response = await _fixture.HttpClient.GetAsync($"/{_fixture.BucketName}/notfound.html");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Custom Error Page", body);
    }

    [Fact]
    public async Task StaticServing_ExistingObject_ServedDirectly()
    {
        const string cssContent = "body { color: red; }";
        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "style.css",
            ContentBody = cssContent,
            ContentType = "text/css"
        });
        await PutHtmlObject("index.html", "<html>index</html>");
        await PutHtmlObject("error.html", "<html>error</html>");
        await ConfigureWebsite();

        var response = await _fixture.HttpClient.GetAsync($"/{_fixture.BucketName}/style.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/css", response.Content.Headers.ContentType?.MediaType ?? "");
    }

    [Fact]
    public async Task RoutingRule_PrefixRedirect_Returns301()
    {
        await PutHtmlObject("index.html", "<html>index</html>");
        await PutHtmlObject("error.html", "<html>error</html>");

        var routingRules = new List<RoutingRule>
        {
            new RoutingRule
            {
                Condition = new RoutingRuleCondition { KeyPrefixEquals = "old/" },
                Redirect = new RoutingRuleRedirect { ReplaceKeyPrefixWith = "new/" }
            }
        };

        await ConfigureWebsite(routingRules: routingRules);

        var response = await _noRedirectClient.GetAsync($"/{_fixture.BucketName}/old/page.html");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("new/page.html", location);
    }

    [Fact]
    public async Task RedirectAllRequestsTo_Returns301()
    {
        var config = new WebsiteConfiguration
        {
            RedirectAllRequestsTo = new RoutingRuleRedirect
            {
                HostName = "example.com",
                Protocol = "https"
            }
        };

        await _fixture.S3Client.PutBucketWebsiteAsync(new PutBucketWebsiteRequest
        {
            BucketName = _fixture.BucketName,
            WebsiteConfiguration = config
        });

        var response = await _noRedirectClient.GetAsync($"/{_fixture.BucketName}/anything");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("example.com", location);
        Assert.StartsWith("https://", location);
    }
}
