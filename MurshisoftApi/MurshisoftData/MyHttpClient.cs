using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace MurshisoftData;

public  class MyHttpClient
{
    public static IHttpClientFactory httpFactory = new ServiceCollection()
            .AddHttpClient()
            .BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>();

    public static HttpClient GetHttpClient(string url)
    {
        var client = httpFactory.CreateClient();
        if(url!=null)
        client.BaseAddress = new Uri(url);
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }
}
