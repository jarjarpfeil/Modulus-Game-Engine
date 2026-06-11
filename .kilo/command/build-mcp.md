---
description: Build the ModulusEngine MCP server
---
Build the C# MCP server (HTTP client to the engine's localhost:9876 API).

```bash
dotnet build tools/ModulusEngine.MCPServer/ModulusEngine.MCPServer.csproj \
  -p:StrideNativeWindowsArm64Enabled=false
```

To run it (requires a running engine on port 9876):

```bash
dotnet run --project tools/ModulusEngine.MCPServer/ModulusEngine.MCPServer.csproj --no-build
```

The server uses stdio transport. The Kilo config registers it as `mcp.modulus`.
