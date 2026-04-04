using CosmoApiServer.Core.Hosting;

namespace CosmoApiServer.Core.Tests.Hosting;

public class ServerOptionsTests
{
    [Fact]
    public void ServerOptions_Http3MaxRequestsPerConnection_DefaultIs100()
    {
        var options = new ServerOptions();
        Assert.Equal(100, options.Http3MaxRequestsPerConnection);
    }

    [Fact]
    public void ServerOptions_Http3MaxConcurrentStreams_DefaultIs100()
    {
        var options = new ServerOptions();
        Assert.Equal(100, options.Http3MaxConcurrentStreams);
    }

    [Fact]
    public void ServerOptions_Http3MaxRequestsPerConnection_CanBeCustomized()
    {
        var options = new ServerOptions { Http3MaxRequestsPerConnection = 500 };
        Assert.Equal(500, options.Http3MaxRequestsPerConnection);
    }

    [Fact]
    public void ServerOptions_Http3MaxConcurrentStreams_CanBeCustomized()
    {
        var options = new ServerOptions { Http3MaxConcurrentStreams = 200 };
        Assert.Equal(200, options.Http3MaxConcurrentStreams);
    }

    [Fact]
    public void ServerOptions_Http3MaxRequestsPerConnection_ZeroDisablesLimit()
    {
        // Zero is a valid value meaning "no per-connection limit"
        var options = new ServerOptions { Http3MaxRequestsPerConnection = 0 };
        Assert.Equal(0, options.Http3MaxRequestsPerConnection);
    }

    [Fact]
    public void ServerOptions_EnableHttp3_DefaultIsFalse()
    {
        var options = new ServerOptions();
        Assert.False(options.EnableHttp3);
    }

    [Fact]
    public void ServerOptions_EnableHttp2_DefaultIsFalse()
    {
        var options = new ServerOptions();
        Assert.False(options.EnableHttp2);
    }

    [Fact]
    public void ServerOptions_Http3IdleTimeoutSeconds_DefaultIs30()
    {
        var options = new ServerOptions();
        Assert.Equal(30, options.Http3IdleTimeoutSeconds);
    }

    [Fact]
    public void ServerOptions_Http3MaxUnidirectionalStreams_DefaultIs10()
    {
        var options = new ServerOptions();
        Assert.Equal(10, options.Http3MaxUnidirectionalStreams);
    }

    [Fact]
    public void ServerOptions_Http3MaxFieldSectionSize_Default16KB()
    {
        var options = new ServerOptions();
        Assert.Equal(16 * 1024, options.Http3MaxFieldSectionSize);
    }

    [Fact]
    public void ServerOptions_Http3IdleTimeoutSeconds_CanBeCustomized()
    {
        var options = new ServerOptions { Http3IdleTimeoutSeconds = 60 };
        Assert.Equal(60, options.Http3IdleTimeoutSeconds);
    }

    [Fact]
    public void ServerOptions_Http3MaxFieldSectionSize_CanBeCustomized()
    {
        var options = new ServerOptions { Http3MaxFieldSectionSize = 8 * 1024 };
        Assert.Equal(8192, options.Http3MaxFieldSectionSize);
    }
}
