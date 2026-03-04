using System.Threading.Channels;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;

namespace FeatureShowcase.Controllers;

[Route("api/stream")]
public class StreamController(ChannelReader<string> reader) : ControllerBase
{
    /// <summary>
    /// Streams messages from a background Channel directly to the client as NDJSON.
    /// </summary>
    [HttpGet("events")]
    public IAsyncEnumerable<string> GetEvents()
    {
        // ReadAllAsync returns IAsyncEnumerable<string>
        // CosmoApiServer will stream each item as a JSON chunk.
        return reader.ReadAllAsync();
    }
}
