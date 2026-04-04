using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Transport;

namespace CosmoApiServer.Core.Tests.Transport;

public class Http11WriterTests
{
    private static async Task<string> FlushAndReadAsync(Pipe pipe)
    {
        pipe.Writer.Complete();
        var result = await pipe.Reader.ReadAsync();
        var text = Encoding.UTF8.GetString(result.Buffer.ToArray());
        pipe.Reader.AdvanceTo(result.Buffer.End);
        return text;
    }

    [Fact]
    public async Task WriteHeaders_InjectsAltSvc_WhenValueProvided()
    {
        var pipe = new Pipe();
        var response = new HttpResponse();
        const string altSvc = "h3=\":443\"; ma=86400";

        Http11Writer.WriteHeaders(pipe.Writer, response, contentLength: 0, altSvcValue: altSvc);
        string output = await FlushAndReadAsync(pipe);

        Assert.Contains($"Alt-Svc: {altSvc}", output);
    }

    [Fact]
    public async Task WriteHeaders_DoesNotInjectAltSvc_WhenValueIsNull()
    {
        var pipe = new Pipe();
        var response = new HttpResponse();

        Http11Writer.WriteHeaders(pipe.Writer, response, contentLength: 0, altSvcValue: null);
        string output = await FlushAndReadAsync(pipe);

        Assert.DoesNotContain("Alt-Svc:", output);
    }

    [Fact]
    public async Task WriteHeaders_DoesNotOverwriteExistingAltSvc_WhenAlreadySetInResponse()
    {
        var pipe = new Pipe();
        var response = new HttpResponse();
        response.Headers["Alt-Svc"] = "h3=\":8443\"; ma=3600";
        const string injectedAltSvc = "h3=\":443\"; ma=86400";

        Http11Writer.WriteHeaders(pipe.Writer, response, contentLength: 0, altSvcValue: injectedAltSvc);
        string output = await FlushAndReadAsync(pipe);

        // The response-set value appears, the injected one does not replace it
        Assert.Contains("h3=\":8443\"; ma=3600", output);
        // Injected value was suppressed since response already has Alt-Svc
        Assert.DoesNotContain(injectedAltSvc, output);
    }

    [Fact]
    public async Task WriteHeaders_AltSvc_AppearsBeforeBlankLine()
    {
        var pipe = new Pipe();
        var response = new HttpResponse();
        const string altSvc = "h3=\":443\"; ma=86400";

        Http11Writer.WriteHeaders(pipe.Writer, response, contentLength: 0, altSvcValue: altSvc);
        string output = await FlushAndReadAsync(pipe);

        int altSvcPos = output.IndexOf("Alt-Svc:", StringComparison.Ordinal);
        int blankLinePos = output.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        Assert.True(altSvcPos >= 0 && blankLinePos >= 0, "Both Alt-Svc and blank line should be present");
        Assert.True(altSvcPos < blankLinePos, "Alt-Svc must appear before the blank line that terminates headers");
    }

    [Fact]
    public async Task WriteHeaders_WritesStatusLine_WithAltSvc()
    {
        var pipe = new Pipe();
        var response = new HttpResponse { StatusCode = 200 };
        const string altSvc = "h3=\":443\"; ma=86400";

        Http11Writer.WriteHeaders(pipe.Writer, response, contentLength: 5, altSvcValue: altSvc);
        string output = await FlushAndReadAsync(pipe);

        Assert.StartsWith("HTTP/1.1 200 OK", output);
        Assert.Contains($"Alt-Svc: {altSvc}", output);
    }
}
