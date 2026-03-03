using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace CosmoS3.Tests;

public class S3Fixture : IAsyncLifetime
{
    public string BucketName { get; } = $"test-{Guid.NewGuid().ToString("N")[..8]}";
    public string EndpointUrl { get; } = "http://localhost:8100";
    public AmazonS3Client S3Client { get; }
    public HttpClient HttpClient { get; }

    public S3Fixture()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = EndpointUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
        };

        S3Client = new AmazonS3Client("default", "default", config);
        HttpClient = new HttpClient { BaseAddress = new Uri(EndpointUrl) };
    }

    public async Task InitializeAsync()
    {
        await S3Client.PutBucketAsync(new PutBucketRequest
        {
            BucketName = BucketName,
            UseClientRegion = true
        });
    }

    public async Task DisposeAsync()
    {
        try
        {
            // Delete all objects first
            var listResponse = await S3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = BucketName
            });

            if (listResponse.S3Objects.Count > 0)
            {
                var deleteRequest = new DeleteObjectsRequest
                {
                    BucketName = BucketName,
                    Objects = listResponse.S3Objects
                        .Select(o => new KeyVersion { Key = o.Key })
                        .ToList()
                };
                await S3Client.DeleteObjectsAsync(deleteRequest);
            }

            // Delete any in-progress multipart uploads
            var uploadsResponse = await S3Client.ListMultipartUploadsAsync(new ListMultipartUploadsRequest
            {
                BucketName = BucketName
            });

            foreach (var upload in uploadsResponse.MultipartUploads)
            {
                await S3Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
                {
                    BucketName = BucketName,
                    Key = upload.Key,
                    UploadId = upload.UploadId
                });
            }

            await S3Client.DeleteBucketAsync(new DeleteBucketRequest
            {
                BucketName = BucketName
            });
        }
        catch
        {
            // Best-effort cleanup
        }
        finally
        {
            S3Client.Dispose();
            HttpClient.Dispose();
        }
    }
}
