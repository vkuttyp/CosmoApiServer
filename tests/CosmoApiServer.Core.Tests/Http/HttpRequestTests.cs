using System.Text;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Tests.Http;

public class HttpRequestTests
{
    [Fact]
    public void ReadForm_DecodesPlusAsSpace()
    {
        var body = Encoding.UTF8.GetBytes("sql=select+*+from+sys.objects&limit=10");
        var request = new HttpRequest { Body = body };

        var form = request.ReadForm();

        Assert.Equal("select * from sys.objects", form.Fields["sql"]);
        Assert.Equal("10", form.Fields["limit"]);
    }

    [Fact]
    public void ReadForm_DecodesPercentEncoding()
    {
        var body = Encoding.UTF8.GetBytes("name=John%20Doe&city=New%20York");
        var request = new HttpRequest { Body = body };

        var form = request.ReadForm();

        Assert.Equal("John Doe", form.Fields["name"]);
        Assert.Equal("New York", form.Fields["city"]);
    }
}
