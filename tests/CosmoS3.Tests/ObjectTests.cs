using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;

namespace CosmoS3.Tests;

public class ObjectTests : IClassFixture<S3Fixture>
{
    private readonly S3Fixture _fixture;

    public ObjectTests(S3Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PutObject_Succeeds()
    {
        var response = await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "test-key.txt",
            ContentBody = "Hello CosmoS3",
            ContentType = "text/plain"
        });
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
    }

    [Fact]
    public async Task GetObject_ReturnsUploadedContent()
    {
        const string content = "Hello CosmoS3";
        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "get-test.txt",
            ContentBody = content,
            ContentType = "text/plain"
        });

        using var response = await _fixture.S3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "get-test.txt"
        });

        using var reader = new StreamReader(response.ResponseStream);
        var body = await reader.ReadToEndAsync();
        Assert.Equal(content, body);
    }

    [Fact]
    public async Task HeadObject_ReturnsMetadata()
    {
        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "head-test.txt",
            ContentBody = "Head me",
            ContentType = "text/plain"
        });

        var response = await _fixture.S3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _fixture.BucketName,
            Key = "head-test.txt"
        });

        Assert.Equal("text/plain", response.Headers.ContentType);
        Assert.True(response.Headers.ContentLength > 0);
    }

    [Fact]
    public async Task ListObjects_ReturnsUploadedObject()
    {
        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "list-v1-test.txt",
            ContentBody = "list me",
            ContentType = "text/plain"
        });

        var response = await _fixture.S3Client.ListObjectsAsync(new ListObjectsRequest
        {
            BucketName = _fixture.BucketName
        });

        Assert.Contains(response.S3Objects, o => o.Key == "list-v1-test.txt");
    }

    [Fact]
    public async Task ListObjectsV2_ReturnsUploadedObject()
    {
        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "list-v2-test.txt",
            ContentBody = "list me v2",
            ContentType = "text/plain"
        });

        var response = await _fixture.S3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = _fixture.BucketName
        });

        Assert.Contains(response.S3Objects, o => o.Key == "list-v2-test.txt");
    }

    [Fact]
    public async Task DeleteObject_Succeeds()
    {
        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "delete-me.txt",
            ContentBody = "bye",
            ContentType = "text/plain"
        });

        await _fixture.S3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "delete-me.txt"
        });

        var ex = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _fixture.S3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _fixture.BucketName,
                Key = "delete-me.txt"
            }));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteMultipleObjects_Succeeds()
    {
        var keys = new[] { "del-multi-1.txt", "del-multi-2.txt", "del-multi-3.txt" };
        foreach (var key in keys)
        {
            await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _fixture.BucketName,
                Key = key,
                ContentBody = "content",
                ContentType = "text/plain"
            });
        }

        await _fixture.S3Client.DeleteObjectsAsync(new DeleteObjectsRequest
        {
            BucketName = _fixture.BucketName,
            Objects = keys.Select(k => new KeyVersion { Key = k }).ToList()
        });

        foreach (var key in keys)
        {
            var ex = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
                await _fixture.S3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _fixture.BucketName,
                    Key = key
                }));
            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }
    }

    [Fact]
    public async Task CopyObject_Succeeds()
    {
        const string sourceKey = "copy-source.txt";
        const string destKey = "copy-dest.txt";
        const string content = "copy this content";

        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = sourceKey,
            ContentBody = content,
            ContentType = "text/plain"
        });

        await _fixture.S3Client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = _fixture.BucketName,
            SourceKey = sourceKey,
            DestinationBucket = _fixture.BucketName,
            DestinationKey = destKey
        });

        using var response = await _fixture.S3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = destKey
        });

        using var reader = new StreamReader(response.ResponseStream);
        var body = await reader.ReadToEndAsync();
        Assert.Equal(content, body);
    }

    [Fact]
    public async Task PutObject_LargeObject()
    {
        const int size = 5 * 1024 * 1024; // 5MB
        var data = new byte[size];
        new Random(42).NextBytes(data);

        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "large-object.bin",
            InputStream = new MemoryStream(data),
            ContentType = "application/octet-stream"
        });

        var meta = await _fixture.S3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _fixture.BucketName,
            Key = "large-object.bin"
        });

        Assert.Equal(size, meta.Headers.ContentLength);
    }

    [Fact]
    public async Task GetObject_WithRangeRequest()
    {
        const string content = "Hello CosmoS3 Range Test";
        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "range-test.txt",
            ContentBody = content,
            ContentType = "text/plain"
        });

        using var response = await _fixture.S3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _fixture.BucketName,
            Key = "range-test.txt",
            ByteRange = new ByteRange(0, 4)
        });

        using var reader = new StreamReader(response.ResponseStream);
        var partial = await reader.ReadToEndAsync();
        Assert.Equal("Hello", partial);
    }
}
