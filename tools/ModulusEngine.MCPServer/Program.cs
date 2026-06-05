using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModulusEngine.MCPServer.Bridge;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// MCP requires logs on stderr to avoid corrupting stdio transport
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register the engine bridge (HTTP client to engine API)
builder.Services.AddHttpClient<EngineBridge>(client =>
{
    client.BaseAddress = new Uri("http://localhost:9876/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Register MCP server with stdio transport, scan for tools
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
