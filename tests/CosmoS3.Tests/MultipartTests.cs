using Amazon.S3;
using Amazon.S3.Model;

namespace CosmoS3.Tests;

public class MultipartTests : IClassFixture<S3Fixture>
{
    private readonly S3Fixture _fixture;

    public MultipartTests(S3Fixture fixture)
    {
        _fixture = fixture;
    }

    private static byte[] CreatePartData(int sizeBytes)
    {
        var data = new byte[sizeBytes];
        new Random(42).NextBytes(data);
        return data;
    }

    [Fact]
    public async Task MultipartUpload_FullLifecycle()
    {
        const string key = "multipart-full.bin";
        const int partSize = 5 * 1024 * 1024; // 5MB
        const int partCount = 3;

        // 1. Initiate
        var initResponse = await _fixture.S3Client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            ContentType = "application/octet-stream"
        });
        var uploadId = initResponse.UploadId;

        // 2. Upload parts
        var partETags = new List<PartETag>();
        for (int i = 1; i <= partCount; i++)
        {
            var data = CreatePartData(partSize);
            var partResponse = await _fixture.S3Client.UploadPartAsync(new UploadPartRequest
            {
                BucketName = _fixture.BucketName,
                Key = key,
                UploadId = uploadId,
                PartNumber = i,
                InputStream = new MemoryStream(data),
                PartSize = partSize
            });
            partETags.Add(new PartETag(i, partResponse.ETag));
        }

        // 3. List parts
        var listPartsResponse = await _fixture.S3Client.ListPartsAsync(new ListPartsRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            UploadId = uploadId
        });
        Assert.Equal(partCount, listPartsResponse.Parts.Count);

        // 4. Complete
        await _fixture.S3Client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            UploadId = uploadId,
            PartETags = partETags
        });

        // 5. Verify size
        var meta = await _fixture.S3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _fixture.BucketName,
            Key = key
        });
        Assert.Equal((long)partSize * partCount, meta.Headers.ContentLength);
    }

    [Fact]
    public async Task MultipartUpload_Abort()
    {
        const string key = "multipart-abort.bin";

        // 1. Initiate
        var initResponse = await _fixture.S3Client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = _fixture.BucketName,
            Key = key
        });
        var uploadId = initResponse.UploadId;

        // 2. Upload 1 part
        var data = CreatePartData(5 * 1024 * 1024);
        await _fixture.S3Client.UploadPartAsync(new UploadPartRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            UploadId = uploadId,
            PartNumber = 1,
            InputStream = new MemoryStream(data),
            PartSize = data.Length
        });

        // 3. Abort
        await _fixture.S3Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = _fixture.BucketName,
            Key = key,
            UploadId = uploadId
        });

        // 4. Assert no longer listed
        var listResponse = await _fixture.S3Client.ListMultipartUploadsAsync(new ListMultipartUploadsRequest
        {
            BucketName = _fixture.BucketName
        });
        Assert.DoesNotContain(listResponse.MultipartUploads, u => u.UploadId == uploadId);
    }

    [Fact]
    public async Task MultipartUpload_ListActiveUploads()
    {
        var key1 = $"multipart-active-1-{Guid.NewGuid():N}.bin";
        var key2 = $"multipart-active-2-{Guid.NewGuid():N}.bin";

        // 1. Initiate 2 uploads
        var init1 = await _fixture.S3Client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = _fixture.BucketName,
            Key = key1
        });
        var init2 = await _fixture.S3Client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = _fixture.BucketName,
            Key = key2
        });

        // 2. List and assert both appear
        var listResponse = await _fixture.S3Client.ListMultipartUploadsAsync(new ListMultipartUploadsRequest
        {
            BucketName = _fixture.BucketName
        });

        Assert.Contains(listResponse.MultipartUploads, u => u.UploadId == init1.UploadId);
        Assert.Contains(listResponse.MultipartUploads, u => u.UploadId == init2.UploadId);

        // 3. Abort both
        await _fixture.S3Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = _fixture.BucketName,
            Key = key1,
            UploadId = init1.UploadId
        });
        await _fixture.S3Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = _fixture.BucketName,
            Key = key2,
            UploadId = init2.UploadId
        });
    }
}
