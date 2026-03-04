using System.Net.WebSockets;
using System.Text;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;

namespace FeatureShowcase.Controllers;

public class WebSocketController : ControllerBase
{
    [HttpGet("/ws")]
    public async Task Echo()
    {
        if (HttpContext.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.AcceptWebSocketAsync();
            Console.WriteLine("[WS] Connection accepted");

            await webSocket.SendAsync(
                Encoding.UTF8.GetBytes("Welcome to Cosmo WebSocket Echo!"), 
                WebSocketMessageType.Text, 
                true);

            // In a real app, you would have a receive loop here.
            // Since we've built the high-performance sender, we demonstrate it.
            await Task.Delay(5000); // Keep open for 5s for the demo
            
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Demo complete");
            Console.WriteLine("[WS] Connection closed");
        }
        else
        {
            Response.StatusCode = 400;
            Response.WriteText("Not a WebSocket request");
        }
    }
}
