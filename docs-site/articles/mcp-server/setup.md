# Modulus Engine — MCP Server Setup Guide

> **Server Package:** `ModulusEngine.MCPServer`  
> **Protocol:** Model Context Protocol (MCP) over HTTP  
> **Default Port:** `9876`  
> **Version:** 1.0  
> **Audience:** Developers using AI agents with Modulus Engine

---

## Table of Contents

1. [What is MCP?](#1-what-is-mcp)
2. [Prerequisites](#2-prerequisites)
3. [Installation](#3-installation)
4. [Configuration](#4-configuration)
5. [Starting the Server](#5-starting-the-server)
6. [Connecting from Clients](#6-connecting-from-clients)
   - [Claude Desktop](#claude-desktop)
   - [VS Code (GitHub Copilot)](#vs-code-github-copilot)
   - [Cursor](#cursor)
7. [Available Tools](#7-available-tools)
   - [Scene Tools (scene_*)](#scene-tools-scene_)
   - [Editor Tools (editor_*)](#editor-tools-editor_)
   - [Debug Tools (debug_*)](#debug-tools-debug_)
   - [Mod Tools (mod_*)](#mod-tools-mod_)
8. [Troubleshooting](#8-troubleshooting)

---

## 1. What is MCP?

The **Model Context Protocol (MCP)** is an open standard that allows AI assistants (like Claude, GitHub Copilot, or Cursor's AI) to interact with external tools through a structured interface. Instead of the AI generating code blindly, MCP gives it the ability to **read live state**, **invoke operations**, and **receive real-time feedback** from running applications.

Modulus Engine implements an MCP server that exposes engine functionality — scene manipulation, editor control, debugging, and mod management — as callable tools. This means an AI agent can:

- Query the current scene graph and inspect entities
- Spawn, modify, or remove objects in the running game
- Read editor state (selected objects, viewport info, play mode status)
- Attach debuggers, set breakpoints, and inspect variables
- Load, unload, and hot-reload mods at runtime

### How It Works

```
┌─────────────┐    MCP (HTTP/JSON)    ┌──────────────────┐     IPC/HTTP     ┌─────────────────┐
│  AI Agent   │ ◄──────────────────► │  ModulusEngine.   │ ◄──────────────► │  Modulus Engine  │
│  (Claude,   │     Port 9876        │  MCPServer        │                  │  (Editor/Runtime)│
│   Copilot)  │                      │  (.NET tool)      │                  │                  │
└─────────────┘                      └──────────────────┘                  └─────────────────┘
```

The MCP server acts as a **bridge** — it speaks MCP on one side (to AI agents) and talks to the running Modulus Engine instance on the other.

---

## 2. Prerequisites

Before installing the MCP server, ensure you have:

| Requirement | Version | Check Command |
|-------------|---------|---------------|
| **.NET SDK** | 8.0 or later | `dotnet --version` |
| **Modulus Engine** | Running instance | N/A — start the engine first |
| **AI Client** | Claude Desktop, VS Code, or Cursor | N/A |

The Modulus Engine must be **running** before you start the MCP server. The server connects to the engine at startup and requires an active connection to function.

---

## 3. Installation

The MCP server is distributed as a .NET global tool. Install it with a single command:

```bash
dotnet tool install -g ModulusEngine.MCPServer
```

### Verify Installation

```bash
dotnet tool list -g | grep modulus
```

Expected output:

```
ModulusEngine.MCPServer    1.0.x    modulus-mcp    modulusengine.mcpserver
```

### Updating

When a new version is released, update with:

```bash
dotnet tool update -g ModulusEngine.MCPServer
```

### Uninstalling

```bash
dotnet tool uninstall -g ModulusEngine.MCPServer
```

> **Note:** If the `modulus-mcp` command is not found after installation, ensure your `PATH` includes the .NET tools directory:
> - **Windows:** `%USERPROFILE%\.dotnet\tools`
> - **macOS/Linux:** `~/.dotnet/tools`

---

## 4. Configuration

The MCP server reads its configuration from environment variables or a configuration file.

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `MODULUS_ENGINE_URL` | `http://localhost:7200` | URL of the running Modulus Engine instance |
| `MODULUS_MCP_PORT` | `9876` | Port the MCP server listens on for AI agent connections |
| `MODULUS_MCP_LOG_LEVEL` | `info` | Logging verbosity: `trace`, `debug`, `info`, `warn`, `error` |

### Configuration File

Alternatively, create a `mcp-config.json` in the working directory:

```json
{
  "engineUrl": "http://localhost:7200",
  "port": 9876,
  "logLevel": "info"
}
```

### Custom Engine URL

If your engine runs on a different host or port:

```bash
export MODULUS_ENGINE_URL="http://192.168.1.100:7200"
export MODULUS_MCP_PORT=9876
modulus-mcp
```

Or on Windows:

```cmd
set MODULUS_ENGINE_URL=http://192.168.1.100:7200
set MODULUS_MCP_PORT=9876
modulus-mcp
```

---

## 5. Starting the Server

### Step 1 — Start Modulus Engine

Launch the Modulus Engine editor or runtime **first**. The MCP server needs a running engine to connect to.

### Step 2 — Start the MCP Server

```bash
modulus-mcp
```

Expected output:

```
Modulus Engine MCP Server v1.0.0
Connecting to engine at http://localhost:7200 ... connected.
Listening for MCP clients on http://localhost:9876
Ready.
```

### Step 3 — Connect Your AI Client

Configure your AI client to connect to the MCP server (see [Section 6](#6-connecting-from-clients)).

### Running in the Background

On macOS/Linux:

```bash
modulus-mcp &
```

On Windows, use a separate terminal window, or run as a background process:

```cmd
start /B modulus-mcp
```

---

## 6. Connecting from Clients

### Claude Desktop

Add the MCP server to your Claude Desktop configuration.

**Config file location:**
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`

Add the following to the `mcpServers` section:

```json
{
  "mcpServers": {
    "modulus-engine": {
      "url": "http://localhost:9876",
      "transport": "http"
    }
  }
}
```

Restart Claude Desktop. The Modulus Engine tools will appear automatically in the tool list.

### VS Code (GitHub Copilot)

Add the MCP server to your VS Code settings or workspace configuration.

**Option A — Workspace `.vscode/mcp.json`:**

```json
{
  "servers": {
    "modulus-engine": {
      "type": "http",
      "url": "http://localhost:9876"
    }
  }
}
```

**Option B — User `settings.json`:**

```json
{
  "mcp.servers": {
    "modulus-engine": {
      "type": "http",
      "url": "http://localhost:9876"
    }
  }
}
```

Reload VS Code. Copilot Chat will have access to Modulus Engine tools.

### Cursor

Add the MCP server to your Cursor configuration.

**Config file location:** `~/.cursor/mcp.json`

```json
{
  "mcpServers": {
    "modulus-engine": {
      "url": "http://localhost:9876",
      "transport": "http"
    }
  }
}
```

Restart Cursor. The tools will be available in Cursor's AI assistant.

---

## 7. Available Tools

The MCP server exposes tools organized into four categories. All tool names use snake_case.

### Scene Tools (`scene_*`)

Manipulate the live scene graph.

| Tool | Description |
|------|-------------|
| `scene_list` | List all entities in the current scene |
| `scene_get` | Get detailed info about an entity (components, transform, children) |
| `scene_spawn` | Create a new entity with specified components |
| `scene_destroy` | Remove an entity from the scene |
| `scene_set_transform` | Set position, rotation, and/or scale of an entity |
| `scene_get_transform` | Read the current transform of an entity |
| `scene_add_component` | Add a component to an existing entity |
| `scene_remove_component` | Remove a component from an entity |
| `scene_search` | Search entities by name, tag, or component type |
| `scene_hierarchy` | Get the full scene hierarchy as a tree |

### Editor Tools (`editor_*`)

Interact with the Modulus Editor UI state.

| Tool | Description |
|------|-------------|
| `editor_get_selection` | Get currently selected entities in the editor |
| `editor_set_selection` | Select one or more entities in the editor |
| `editor_get_play_state` | Check if the editor is in Play, Pause, or Edit mode |
| `editor_set_play_state` | Enter or exit Play mode |
| `editor_get_viewport` | Get viewport camera position, rotation, and size |
| `editor_undo` | Undo the last editor action |
| `editor_redo` | Redo the last undone action |
| `editor_save` | Save the current scene/project |
| `editor_get_project_info` | Get project name, path, and active scene info |

### Debug Tools (`debug_*`)

Attach to and inspect the running game.

| Tool | Description |
|------|-------------|
| `debug_get_logs` | Retrieve recent engine/game log output |
| `debug_set_breakpoint` | Set a breakpoint at a source file and line |
| `debug_remove_breakpoint` | Remove a breakpoint |
| `debug_get_variables` | Inspect local/global variables at a breakpoint |
| `debug_step` | Step over / step into / step out while paused |
| `debug_continue` | Resume execution after a breakpoint hit |
| `debug_get_call_stack` | Get the current call stack |
| `debug_profile_frame` | Capture a performance profile of the current frame |
| `debug_inspect_component` | Deep-inspect a component's serialized fields |

### Mod Tools (`mod_*`)

Manage mods at runtime.

| Tool | Description |
|------|-------------|
| `mod_list` | List all loaded and available mods |
| `mod_info` | Get detailed info about a mod (manifest, state, dependencies) |
| `mod_load` | Load a mod into the runtime |
| `mod_unload` | Unload a mod (hot-reload safe) |
| `mod_reload` | Unload and immediately reload a mod |
| `mod_enable` | Enable a disabled mod |
| `mod_disable` | Disable an active mod |
| `mod_get_log` | Get log output from a specific mod |
| `mod_validate` | Validate a mod's manifest and dependencies without loading |

---

## 8. Troubleshooting

### Engine Not Running

**Symptom:** Server fails to start with connection error.

```
Error: Could not connect to engine at http://localhost:7200
```

**Solutions:**
1. Ensure Modulus Engine is running before starting the MCP server.
2. Verify the engine URL is correct:
   ```bash
   curl http://localhost:7200/health
   ```
3. If the engine runs on a different port, set `MODULUS_ENGINE_URL` accordingly.

---

### Port Already in Use

**Symptom:** Server fails to bind with a port conflict error.

```
Error: Address already in use — port 9876
```

**Solutions:**
1. Find what's using the port:
   ```bash
   # Windows
   netstat -ano | findstr :9876
   
   # macOS/Linux
   lsof -i :9876
   ```
2. Kill the conflicting process, or use a different port:
   ```bash
   export MODULUS_MCP_PORT=9877
   modulus-mcp
   ```
   Remember to update the port in your AI client configuration as well.

---

### AI Client Can't See Tools

**Symptom:** The AI client connects but no Modulus tools appear.

**Solutions:**
1. Restart the AI client after updating its configuration.
2. Verify the MCP server is running and listening:
   ```bash
   curl http://localhost:9876/health
   ```
3. Check that the URL and port in the client config match the server output.
4. For Claude Desktop, ensure the JSON config is valid (no trailing commas).

---

### Tools Return Errors

**Symptom:** Tools are visible but return errors when called.

**Solutions:**
1. Check that the engine is still running — the MCP server does not auto-reconnect.
2. Restart the MCP server:
   ```bash
   # Ctrl+C to stop, then:
   modulus-mcp
   ```
3. Enable debug logging for more detail:
   ```bash
   export MODULUS_MCP_LOG_LEVEL=debug
   modulus-mcp
   ```

---

### Firewall / Network Issues (Remote Engine)

**Symptom:** MCP server can't reach a remote engine instance.

**Solutions:**
1. Ensure the engine host allows incoming connections on its port (default `7200`).
2. Check firewall rules:
   ```bash
   # Windows
   netsh advfirewall firewall show rule name=all | findstr "7200"
   
   # Linux
   sudo ufw status | grep 7200
   ```
3. Test connectivity:
   ```bash
   curl http://<engine-host>:7200/health
   ```

---

### .NET Tool Not Found After Install

**Symptom:** `modulus-mcp` command not recognized.

**Solutions:**
1. Verify the install:
   ```bash
   dotnet tool list -g
   ```
2. Ensure `~/.dotnet/tools` (or `%USERPROFILE%\.dotnet\tools` on Windows) is in your `PATH`:
   ```bash
   # Check
   echo $PATH | grep dotnet
   
   # Add permanently (bash/zsh)
   echo 'export PATH="$HOME/.dotnet/tools:$PATH"' >> ~/.bashrc
   source ~/.bashrc
   ```
3. On Windows, add `%USERPROFILE%\.dotnet\tools` to your system `PATH` via System Properties → Environment Variables.

---

## Quick Reference

```bash
# Install
dotnet tool install -g ModulusEngine.MCPServer

# Start (engine must be running)
modulus-mcp

# Start with custom settings
MODULUS_ENGINE_URL=http://localhost:7200 MODULUS_MCP_PORT=9876 modulus-mcp

# Update
dotnet tool update -g ModulusEngine.MCPServer

# Uninstall
dotnet tool uninstall -g ModulusEngine.MCPServer
```

---

## See Also

- [Mod API Reference](./MOD-API-REFERENCE.md) — Complete modding API reference
- [Modding Architecture](./MODULUS_ENGINE_MODDING_ARCHITECTURE.md) — System internals and design
- [Writing a Mod](./writing-a-mod.md) — Getting started with mod development
- [Cross-Game Mods](./CROSS-GAME-MODS.md) — Building mods that work across projects
