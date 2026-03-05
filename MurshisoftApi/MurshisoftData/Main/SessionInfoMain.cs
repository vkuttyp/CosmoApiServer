using Microsoft.Extensions.DependencyInjection;
using MurshisoftData.Models;
using System;
using System.Net.Http;
using System.Text.Json;

namespace MurshisoftData.Main;

public static class SessionInfoMain
{
    public static IHttpClientFactory httpFactory = new ServiceCollection()
        .AddHttpClient()
        .BuildServiceProvider()
        .GetRequiredService<IHttpClientFactory>();

    static HttpClient _cacheApiClient;
    public static JsonSerializerOptions options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };
    
    public static SessionData SessionData;
    public static bool IsHijri { get; set; }
   
}