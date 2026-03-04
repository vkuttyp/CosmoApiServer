using System.Text;
using CosmoApiServer.Core.Http;
using Xunit;

namespace CosmoApiServer.Core.Tests.Http;

public class MultipartParserTests
{
    private static byte[] Body(string boundary, params (string name, string? filename, string? ct, string value)[] parts)
    {
        var sb = new StringBuilder();
        foreach (var (name, filename, partCt, value) in parts)
        {
            sb.Append($"--{boundary}\r\n");
            var disp = filename is not null
                ? $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{filename}\"\r\n"
                : $"Content-Disposition: form-data; name=\"{name}\"\r\n";
            sb.Append(disp);
            if (partCt is not null) sb.Append($"Content-Type: {partCt}\r\n");
            sb.Append("\r\n");
            sb.Append(value);
            sb.Append("\r\n");
        }
        sb.Append($"--{boundary}--\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Fact]
    public void ParsesTextField()
    {
        var body = Body("abc", ("username", null, null, "alice"));
        var form = MultipartParser.Parse(body, "multipart/form-data; boundary=abc");
        Assert.Equal("alice", form.Fields["username"]);
        Assert.Empty(form.Files);
    }

    [Fact]
    public void ParsesMultipleFields()
    {
        var body = Body("abc",
            ("first", null, null, "John"),
            ("last",  null, null, "Doe"));
        var form = MultipartParser.Parse(body, "multipart/form-data; boundary=abc");
        Assert.Equal("John", form.Fields["first"]);
        Assert.Equal("Doe",  form.Fields["last"]);
    }

    [Fact]
    public void ParsesFileUpload()
    {
        var body = Body("abc", ("avatar", "photo.png", "image/png", "PNGDATA"));
        var form = MultipartParser.Parse(body, "multipart/form-data; boundary=abc");
        Assert.Empty(form.Fields);
        var file = form.Files["avatar"];
        Assert.Equal("photo.png", file.Filename);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("PNGDATA",   Encoding.UTF8.GetString(file.Data));
    }

    [Fact]
    public void ParsesMixedFieldsAndFile()
    {
        var body = Body("abc",
            ("name",  null,       null,       "report"),
            ("file",  "data.csv", "text/csv", "a,b,c"));
        var form = MultipartParser.Parse(body, "multipart/form-data; boundary=abc");
        Assert.Equal("report", form.Fields["name"]);
        Assert.Equal("data.csv", form.Files["file"].Filename);
    }

    [Fact]
    public void ThrowsOnNonMultipartContentType()
    {
        Assert.Throws<InvalidOperationException>(
            () => MultipartParser.Parse([], "application/json"));
    }

    [Fact]
    public void ThrowsOnMissingBoundary()
    {
        Assert.Throws<InvalidOperationException>(
            () => MultipartParser.Parse([], "multipart/form-data"));
    }

    [Fact]
    public void ReadMultipartOnHttpRequest()
    {
        const string boundary = "testbound";
        var body = Body(boundary, ("key", null, null, "value"));
        var req = new HttpRequest
        {
            Method  = CosmoApiServer.Core.Http.HttpMethod.POST,
            Path    = "/upload",
            Headers = new Dictionary<string, string>
            {
                ["content-type"] = $"multipart/form-data; boundary={boundary}"
            },
            Body = body
        };
        var form = req.ReadMultipart();
        Assert.Equal("value", form.Fields["key"]);
    }
}
