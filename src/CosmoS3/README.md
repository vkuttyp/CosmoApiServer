# CosmoS3

**CosmoS3** is an Amazon S3–compatible object storage middleware library for [CosmoApiServer](../../README.md). It implements core S3 operations using SQL Server for metadata and the local disk (or a pluggable storage driver) for object data.

---

## Table of Contents

1. [Architecture](#architecture)
2. [Quick Start](#quick-start)
3. [Configuration](#configuration)
4. [Database Schema](#database-schema)
5. [S3 Feature Compatibility](#s3-feature-compatibility)
6. [Static Website Hosting](#static-website-hosting)
7. [Presigned URLs](#presigned-urls)
8. [Multipart Upload](#multipart-upload)
9. [Using with AWS CLI](#using-with-aws-cli)
10. [Running Integration Tests](#running-integration-tests)
11. [Project Structure](#project-structure)

---

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   CosmoApiServer                    │
│  (HTTP listener – DotNetty, port 8100 by default)  │
└──────────────────────┬──────────────────────────────┘
                       │ IMiddleware
                       ▼
┌─────────────────────────────────────────────────────┐
│                  S3Middleware                        │
│  • Parses incoming requests into S3Context          │
│  • Authenticates (SigV4 / SigV2 / presigned)        │
│  • Routes to ServiceHandler / BucketHandler /       │
│    ObjectHandler / AdminHandler                      │
└────────────────┬──────────────────────┬─────────────┘
                 │                      │
     ┌───────────▼──────┐   ┌───────────▼───────────┐
     │   DataAccess      │   │   Storage Driver       │
     │  (SQL Server via  │   │  (DiskStorageDriver    │
     │  CosmoSQLClient)  │   │   ./data/objects/)     │
     └──────────────────┘   └───────────────────────┘
```

**Key types:**

| Type | Role |
|------|------|
| `S3Middleware` | Entry point; implements `IMiddleware` |
| `S3Request` | Parses HTTP request into S3 context (method, bucket, key, auth type) |
| `S3Response` | Writes S3-formatted HTTP responses |
| `S3Context` | Combines `S3Request` + `S3Response` for handler use |
| `BucketManager` | In-memory bucket registry; synced with DB at startup |
| `ConfigManager` | DB lookup helpers (users, credentials, buckets, objects) |
| `AuthManager` | SigV2 / SigV4 / presigned URL authentication |
| `DataAccess` | All SQL stored-proc calls via CosmoSQLClient (with `IMemoryCache`) |
| `DiskStorageDriver` | Reads/writes object bytes to local filesystem |

---

## Quick Start

### 1. Add the CosmoS3Host sample project

A ready-to-run host is included at `samples/CosmoS3Host/`.

```bash
cd samples/CosmoS3Host
dotnet run
```

### 2. Wire CosmoS3 into your own CosmoApiServer app

```csharp
using CosmoApiServer.Core.Hosting;
using CosmoS3;
using CosmoS3.Settings;

var settings = new SettingsBase
{
    RegionString       = "us-east-1",
    ValidateSignatures = false,          // set true in production

    Storage = new StorageSettings
    {
        StorageType   = CosmoS3.Storage.StorageDriverType.Disk,
        DiskDirectory = "./data/objects"  // no trailing slash
    },

    Database = new DatabaseSettings
    {
        Hostname     = "localhost",
        Port         = 1433,
        DatabaseName = "MyDatabase",
        Username     = "sa",
        Password     = "your-password"
    }
};

var app = CosmoWebApplicationBuilder.Create()
    .ListenOn(8100)
    .UseLogging()
    .UseMiddleware(new S3Middleware(settings))
    .Build();

app.Run();
```

---

## Configuration

### `SettingsBase`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ValidateSignatures` | `bool` | `true` | Verify AWS Signature V4/V2 on every request. Disable for local dev only. |
| `BaseDomain` | `string?` | `null` | Set to enable virtual-hosted–style URLs (e.g. `"localhost"`). Leave null for path-style. |
| `RegionString` | `string` | `"us-west-1"` | AWS region identifier returned in responses. |
| `HeaderApiKey` | `string` | `"x-api-key"` | HTTP header name for admin API authentication. |
| `AdminApiKey` | `string` | `"cosmos3admin"` | Secret value expected in `HeaderApiKey` for admin endpoints. |
| `Database` | `DatabaseSettings` | (required) | SQL Server connection details. |
| `Storage` | `StorageSettings` | (required) | Object storage configuration. |
| `Logging` | `LoggingSettings` | default | Log level callbacks. |
| `Debug` | `DebugSettings` | default | Enable extra debug output. |
| `Users` / `Credentials` / `Buckets` | `List<T>` | empty | Seed in-memory data for no-database mode (testing). |

### `DatabaseSettings`

| Property | Default | Description |
|----------|---------|-------------|
| `Hostname` | — | SQL Server hostname or IP |
| `Port` | `0` | TCP port (use `1433` for SQL Server) |
| `DatabaseName` | — | Database name |
| `Username` | — | SQL login |
| `Password` | — | SQL password |

The connection string is constructed as:
```
server=HOSTNAME,PORT;database=DBNAME;user id=USER;password=PASS;TrustServerCertificate=true;
```

### `StorageSettings`

| Property | Default | Description |
|----------|---------|-------------|
| `StorageType` | `Disk` | `Disk` is the only currently supported driver |
| `DiskDirectory` | `"./disk/"` | Root directory for object files (no trailing slash) |
| `TempDirectory` | `"./temp/"` | Scratch directory for multipart upload assembly |

---

## Database Schema

CosmoS3 requires a SQL Server database with the `s3` schema. The schema is created by running the migration scripts in `data/`. Key tables:

| Table | Purpose |
|-------|---------|
| `s3.users` | S3 user accounts |
| `s3.credentials` | Access key / secret key pairs linked to users |
| `s3.buckets` | Bucket metadata (name, owner, region, ACL) |
| `s3.objects` | Object metadata (key, size, ETag, content type) |
| `s3.objecttags` | Per-object tags |
| `s3.buckettags` | Per-bucket tags |
| `s3.multipartuploads` | Active multipart upload sessions |
| `s3.multipartparts` | Uploaded parts for active sessions |

All database operations go through stored procedures in the `s3` schema.

---

## S3 Feature Compatibility

### Service-Level Operations

| Operation | AWS CLI command | Status |
|-----------|----------------|--------|
| List Buckets | `aws s3 ls` | ✅ |

### Bucket Operations

| Operation | AWS CLI command | Status |
|-----------|----------------|--------|
| Create Bucket | `aws s3 mb s3://bucket` | ✅ |
| Delete Bucket | `aws s3 rb s3://bucket` | ✅ |
| List Objects (v1 & v2) | `aws s3 ls s3://bucket/` | ✅ |
| Get Bucket ACL | `aws s3api get-bucket-acl` | ✅ |
| Put Bucket ACL | `aws s3api put-bucket-acl` | ✅ |
| Get Bucket Tags | `aws s3api get-bucket-tagging` | ✅ |
| Put Bucket Tags | `aws s3api put-bucket-tagging` | ✅ |
| Delete Bucket Tags | `aws s3api delete-bucket-tagging` | ✅ |
| Get/Put/Delete Bucket Website | `aws s3api *-bucket-website` | ✅ |
| Get Bucket Location | `aws s3api get-bucket-location` | ✅ |
| Get Bucket Versioning | `aws s3api get-bucket-versioning` | ✅ |

### Object Operations

| Operation | AWS CLI command | Status |
|-----------|----------------|--------|
| Put Object | `aws s3 cp local.txt s3://bucket/key` | ✅ |
| Get Object | `aws s3 cp s3://bucket/key local.txt` | ✅ |
| Head Object | `aws s3api head-object` | ✅ |
| Delete Object | `aws s3 rm s3://bucket/key` | ✅ |
| Delete Objects (batch) | `aws s3 sync --delete` | ✅ |
| Copy Object | `aws s3 cp s3://src s3://dst` | ✅ |
| Get Object ACL | `aws s3api get-object-acl` | ✅ |
| Put Object ACL | `aws s3api put-object-acl` | ✅ |
| Get Object Tags | `aws s3api get-object-tagging` | ✅ |
| Put Object Tags | `aws s3api put-object-tagging` | ✅ |
| Delete Object Tags | `aws s3api delete-object-tagging` | ✅ |
| Presigned GET/PUT URLs | SDK `GetPreSignedURL` | ✅ |
| Multipart Upload | `aws s3 cp` (large files) | ✅ |
| List Multipart Uploads | `aws s3api list-multipart-uploads` | ✅ |
| Abort Multipart Upload | `aws s3api abort-multipart-upload` | ✅ |

### Notes on Compatibility

- **Signature versions**: Both SigV4 and SigV2 are supported for authentication and presigned URLs.
- **`aws-chunked` transfer encoding**: Automatically decoded; works with `aws s3 cp` for any file size.
- **Versioning**: Version IDs are not supported; all operations act on the current (only) version.
- **Bucket policies / CORS / lifecycle / replication**: Not implemented.

---

## Static Website Hosting

A bucket can be configured to serve static files over plain HTTP (no AWS credentials required).

### Configure a bucket for website hosting

```bash
# Create bucket
aws --endpoint-url http://localhost:8100 s3 mb s3://my-site

# Upload content
aws --endpoint-url http://localhost:8100 s3 cp index.html  s3://my-site/index.html  --content-type text/html
aws --endpoint-url http://localhost:8100 s3 cp error.html  s3://my-site/error.html   --content-type text/html

# Enable website hosting
aws --endpoint-url http://localhost:8100 s3 website s3://my-site \
    --index-document index.html \
    --error-document error.html
```

### Browse the site

```bash
# Bucket root returns index.html
curl http://localhost:8100/my-site/

# Unknown path returns error.html with 404
curl http://localhost:8100/my-site/missing.html
```

### Redirect all requests

```bash
aws --endpoint-url http://localhost:8100 s3api put-bucket-website \
    --bucket my-site \
    --website-configuration '{
        "RedirectAllRequestsTo": { "HostName": "example.com", "Protocol": "https" }
    }'
```

### Routing rules

```bash
aws --endpoint-url http://localhost:8100 s3api put-bucket-website \
    --bucket my-site \
    --website-configuration '{
        "IndexDocument": { "Suffix": "index.html" },
        "ErrorDocument": { "Key": "error.html" },
        "RoutingRules": [
            {
                "Condition": { "KeyPrefixEquals": "old/" },
                "Redirect":  { "ReplaceKeyPrefixWith": "new/" }
            }
        ]
    }'
```

**How it works:**

- Website configuration is stored as `website.xml` at `<DiskDirectory>/<bucketName>/website.xml`.
- Requests to a website-enabled bucket without AWS authentication headers are served as static files.
- If the request path ends with `/`, the index document is served.
- If the object is not found, the error document is returned with HTTP 404.
- Redirect rules are evaluated before object lookup.

---

## Presigned URLs

Presigned URLs grant time-limited access to an S3 object without requiring the caller to have AWS credentials.

### Generate a presigned URL (C# SDK)

```csharp
var request = new GetPreSignedUrlRequest
{
    BucketName = "my-bucket",
    Key        = "my-object.txt",
    Expires    = DateTime.UtcNow.AddMinutes(15),
    Verb       = HttpVerb.GET
};

string url = s3Client.GetPreSignedURL(request);
```

### Use the presigned URL

```bash
# Download with curl (no AWS credentials needed)
curl "<presigned-url>" -o downloaded.txt

# Upload with a presigned PUT URL
curl -X PUT "<presigned-put-url>" --data-binary @file.txt
```

**Signature version behavior:**

AWSSDK generates **SigV2** presigned URLs for custom (non-AWS) endpoints. CosmoS3 validates both:

| Version | Query params |
|---------|-------------|
| SigV2 | `AWSAccessKeyId`, `Signature`, `Expires` (Unix timestamp) |
| SigV4 | `X-Amz-Credential`, `X-Amz-Signature`, `X-Amz-Expires` |

Expired presigned URLs return `HTTP 403 ExpiredToken`.

---

## Multipart Upload

Multipart upload allows large files to be uploaded in parts and assembled server-side.

### Via AWS CLI (automatic for files > 8 MB by default)

```bash
# CosmoS3 handles chunked uploads transparently
aws --endpoint-url http://localhost:8100 \
    s3 cp large-file.bin s3://my-bucket/large-file.bin \
    --expected-size 1073741824   # hint for 1 GB file
```

### Via SDK (manual)

```csharp
// 1. Initiate upload
var initResponse = await s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
{
    BucketName  = "my-bucket",
    Key         = "my-object"
});
string uploadId = initResponse.UploadId;

// 2. Upload parts (minimum 5 MB each, except the last)
var uploadPartResponse = await s3.UploadPartAsync(new UploadPartRequest
{
    BucketName   = "my-bucket",
    Key          = "my-object",
    UploadId     = uploadId,
    PartNumber   = 1,
    InputStream  = partStream,
    PartSize     = partStream.Length
});

// 3. Complete the upload
await s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
{
    BucketName = "my-bucket",
    Key        = "my-object",
    UploadId   = uploadId,
    PartETags  = new List<PartETag> { new PartETag(1, uploadPartResponse.ETag) }
});
```

**Internals:**

- Part data is stored temporarily in `TempDirectory` during upload.
- On `CompleteMultipartUpload`, parts are assembled and written to `DiskDirectory`.
- Incomplete uploads are tracked in `s3.multipartuploads` and can be aborted or listed.

---

## Using with AWS CLI

### Configure the AWS CLI for local use

```bash
aws configure
# AWS Access Key ID:     default
# AWS Secret Access Key: default
# Default region name:   us-east-1
# Default output format: json
```

### Common commands

```bash
ENDPOINT=http://localhost:8100

# List buckets
aws --endpoint-url $ENDPOINT s3 ls

# Create bucket
aws --endpoint-url $ENDPOINT s3 mb s3://my-bucket

# Upload file
aws --endpoint-url $ENDPOINT s3 cp file.txt s3://my-bucket/

# Download file
aws --endpoint-url $ENDPOINT s3 cp s3://my-bucket/file.txt ./

# List objects
aws --endpoint-url $ENDPOINT s3 ls s3://my-bucket/

# Sync directory
aws --endpoint-url $ENDPOINT s3 sync ./local-dir/ s3://my-bucket/prefix/

# Delete object
aws --endpoint-url $ENDPOINT s3 rm s3://my-bucket/file.txt

# Delete bucket (must be empty)
aws --endpoint-url $ENDPOINT s3 rb s3://my-bucket

# Tag a bucket
aws --endpoint-url $ENDPOINT s3api put-bucket-tagging \
    --bucket my-bucket \
    --tagging '{"TagSet":[{"Key":"env","Value":"dev"}]}'

# Get bucket tags
aws --endpoint-url $ENDPOINT s3api get-bucket-tagging --bucket my-bucket

# Get bucket website config
aws --endpoint-url $ENDPOINT s3api get-bucket-website --bucket my-bucket

# Presigned URL (60 seconds)
aws --endpoint-url $ENDPOINT s3 presign s3://my-bucket/file.txt --expires-in 60
```

---

## Running Integration Tests

The test suite (`tests/CosmoS3.Tests/`) uses xUnit + AWSSDK.S3. Tests require a running CosmoS3 server and a SQL Server database.

### Start the server

```bash
cd samples/CosmoS3Host
dotnet run
```

### Run all tests

```bash
cd tests/CosmoS3.Tests
dotnet test -c Release --logger "console;verbosity=minimal"
```

### Test coverage

| Test file | Tests | Feature area |
|-----------|-------|--------------|
| `BucketTests.cs` | 9 | Bucket CRUD, ACL, tags, location |
| `ObjectTests.cs` | 9 | Object CRUD, ACL, tags, copy |
| `MultipartTests.cs` | 5 | Initiate, upload parts, complete, abort, list |
| `PresignedUrlTests.cs` | 5 | GET / PUT / HEAD presigned, expiry |
| `WebsiteTests.cs` | 9 | Static serving, routing rules, redirect-all |

**Total: 37 tests, all passing.**

### Fixture

`S3Fixture` (`tests/CosmoS3.Tests/S3Fixture.cs`) creates a unique bucket per test class and tears it down after all tests in the class complete.

```csharp
public class S3Fixture : IAsyncLifetime
{
    public IAmazonS3 S3Client { get; }
    public string BucketName  { get; }   // e.g. "test-a1b2c3d4"
    public HttpClient HttpClient { get; } // for non-S3 HTTP assertions
    public string EndpointUrl { get; } = "http://localhost:8100";
    // ...
}
```

---

## Project Structure

```
src/CosmoS3/
├── S3Middleware.cs          # IMiddleware entry point; request routing
├── S3Request.cs             # HTTP → S3 request parsing (auth, path, query)
├── S3Response.cs            # S3-formatted response writer
├── S3Context.cs             # Combined request + response context
├── S3Exception.cs           # Typed S3 error thrown by handlers
├── DataAccess.cs            # All DB stored-proc calls (with IMemoryCache)
├── SerializationHelper.cs   # XML/JSON serialization helpers
├── Settings/
│   ├── Settings.cs          # SettingsBase (top-level configuration)
│   ├── StorageSettings.cs
│   ├── LoggingSettings.cs
│   └── DebugSettings.cs
├── Classes/
│   ├── AuthManager.cs       # SigV2 / SigV4 / presigned authentication
│   ├── BucketManager.cs     # In-memory bucket list + DB-backed lookup
│   ├── BucketClient.cs      # Per-bucket storage driver accessor
│   ├── ConfigManager.cs     # User / credential / bucket / object lookup
│   └── CleanupManager.cs    # Background task: expire stale temp files
├── Api/S3/
│   ├── ApiHandler.cs        # Top-level dispatcher (service/bucket/object)
│   ├── ServiceHandler.cs    # ListBuckets
│   ├── BucketHandler.cs     # All bucket operations
│   ├── ObjectHandler.cs     # All object operations + multipart
│   └── ApiHelper.cs         # Shared XML response helpers
├── Storage/
│   ├── StorageDriverBase.cs
│   └── DiskStorageDriver.cs # Filesystem-based object storage
├── S3Objects/               # XML DTOs (request/response bodies)
│   ├── Error.cs
│   ├── ListAllMyBucketsResult.cs
│   ├── ListBucketResult.cs
│   └── ...
└── Logging/
    └── S3Logger.cs          # Console/callback-based logger
```
