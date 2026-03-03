using Amazon.S3;
using Amazon.S3.Model;

namespace CosmoS3.Tests;

public class BucketTests : IClassFixture<S3Fixture>
{
    private readonly S3Fixture _fixture;

    public BucketTests(S3Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateBucket_Succeeds()
    {
        var bucketName = $"test-create-{Guid.NewGuid().ToString("N")[..8]}";
        try
        {
            var response = await _fixture.S3Client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = bucketName,
                UseClientRegion = true
            });
            Assert.Equal(System.Net.HttpStatusCode.OK, response.HttpStatusCode);
        }
        finally
        {
            await _fixture.S3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucketName });
        }
    }

    [Fact]
    public async Task ListBuckets_ContainsCreatedBucket()
    {
        var response = await _fixture.S3Client.ListBucketsAsync();
        Assert.Contains(response.Buckets, b => b.BucketName == _fixture.BucketName);
    }

    [Fact]
    public async Task HeadBucket_Exists()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head,
            $"/{_fixture.BucketName}");
        // Sign via SDK by fetching location instead
        var response = await _fixture.S3Client.GetBucketLocationAsync(new GetBucketLocationRequest
        {
            BucketName = _fixture.BucketName
        });
        Assert.NotNull(response);
    }

    [Fact]
    public async Task DeleteBucket_Succeeds()
    {
        var bucketName = $"test-del-{Guid.NewGuid().ToString("N")[..8]}";
        await _fixture.S3Client.PutBucketAsync(new PutBucketRequest
        {
            BucketName = bucketName,
            UseClientRegion = true
        });

        await _fixture.S3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucketName });

        var ex = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _fixture.S3Client.GetBucketLocationAsync(new GetBucketLocationRequest
            {
                BucketName = bucketName
            }));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task CreateBucket_DuplicateName_ReturnsBucketAlreadyExistsOrSucceeds()
    {
        // S3 allows idempotent creates by the same owner; some implementations return BucketAlreadyExists
        try
        {
            var response = await _fixture.S3Client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = _fixture.BucketName,
                UseClientRegion = true
            });
            // Idempotent success is acceptable
            Assert.Equal(System.Net.HttpStatusCode.OK, response.HttpStatusCode);
        }
        catch (AmazonS3Exception ex)
        {
            Assert.True(
                ex.ErrorCode == "BucketAlreadyExists" || ex.ErrorCode == "BucketAlreadyOwnedByYou",
                $"Unexpected error code: {ex.ErrorCode}");
        }
    }

    [Fact]
    public async Task GetBucketAcl_ReturnsAcl()
    {
        var response = await _fixture.S3Client.GetACLAsync(new GetACLRequest
        {
            BucketName = _fixture.BucketName
        });
        Assert.NotNull(response.AccessControlList.Owner);
    }

    [Fact]
    public async Task PutBucketTagging_And_GetBucketTagging()
    {
        var tags = new List<Tag>
        {
            new Tag { Key = "env", Value = "test" },
            new Tag { Key = "project", Value = "cosmos3" }
        };

        await _fixture.S3Client.PutBucketTaggingAsync(new PutBucketTaggingRequest
        {
            BucketName = _fixture.BucketName,
            TagSet = tags
        });

        var response = await _fixture.S3Client.GetBucketTaggingAsync(new GetBucketTaggingRequest
        {
            BucketName = _fixture.BucketName
        });

        Assert.Contains(response.TagSet, t => t.Key == "env" && t.Value == "test");
        Assert.Contains(response.TagSet, t => t.Key == "project" && t.Value == "cosmos3");
    }

    [Fact]
    public async Task DeleteBucketTagging_Succeeds()
    {
        await _fixture.S3Client.PutBucketTaggingAsync(new PutBucketTaggingRequest
        {
            BucketName = _fixture.BucketName,
            TagSet = new List<Tag> { new Tag { Key = "temp", Value = "yes" } }
        });

        await _fixture.S3Client.DeleteBucketTaggingAsync(new DeleteBucketTaggingRequest
        {
            BucketName = _fixture.BucketName
        });

        try
        {
            var response = await _fixture.S3Client.GetBucketTaggingAsync(new GetBucketTaggingRequest
            {
                BucketName = _fixture.BucketName
            });
            Assert.Empty(response.TagSet);
        }
        catch (AmazonS3Exception ex)
        {
            Assert.Equal("NoSuchTagSet", ex.ErrorCode);
        }
    }
}
