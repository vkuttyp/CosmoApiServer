using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;

namespace CosmoS3.Tests;

public class PresignedUrlTests : IClassFixture<S3Fixture>
{
    private readonly S3Fixture _fixture;

    public PresignedUrlTests(S3Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PresignedGet_ReturnsContent()
    {
        const string key = "presigned-get.txt";
        const string content = "presigned get content";

        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            ContentBody = content,
            ContentType = "text/plain"
        });

        var url = _fixture.S3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddSeconds(60)
        });

        var response = await _fixture.HttpClient.GetAsync(new Uri(url).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(content, body);
    }

    [Fact]
    public async Task PresignedPut_UploadsContent()
    {
        const string key = "presigned-put.txt";
        const string content = "presigned put content";

        var url = _fixture.S3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddSeconds(60),
            ContentType = "text/plain"
        });

        using var putContent = new StringContent(content, Encoding.UTF8, "text/plain");
        var putResponse = await _fixture.HttpClient.PutAsync(new Uri(url).PathAndQuery, putContent);
        Assert.True(putResponse.IsSuccessStatusCode, $"PUT failed: {putResponse.StatusCode}");

        using var getResponse = await _fixture.S3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = key
        });

        using var reader = new StreamReader(getResponse.ResponseStream);
        var body = await reader.ReadToEndAsync();
        Assert.Equal(content, body);
    }

    [Fact]
    public async Task PresignedUrl_Expired_Returns400OrForbidden()
    {
        const string key = "presigned-expired.txt";

        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            ContentBody = "expiry test",
            ContentType = "text/plain"
        });

        var url = _fixture.S3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddSeconds(1)
        });

        await Task.Delay(TimeSpan.FromSeconds(3));

        var response = await _fixture.HttpClient.GetAsync(new Uri(url).PathAndQuery);
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 400 or 403 but got {response.StatusCode}");
    }
}
