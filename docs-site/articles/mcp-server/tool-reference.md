# Modulus Engine — MCP Tool Reference

> **Version:** 1.0  
> **Last Updated:** 2026-06-09  
> **Purpose:** Complete reference for all Model Context Protocol (MCP) tools exposed by the Modulus Engine editor and runtime.

---

## Table of Contents

1. [Scene Tools](#scene-tools)
   - [`scene_get_entities`](#scene_get_entities)
   - [`scene_create_entity`](#scene_create_entity)
   - [`scene_delete_entity`](#scene_delete_entity)
2. [Editor Tools](#editor-tools)
   - [`editor_get_status`](#editor_get_status)
   - [`editor_take_screenshot`](#editor_take_screenshot)
3. [Debug Tools](#debug-tools)
   - [`debug_get_logs`](#debug_get_logs)
   - [`debug_get_console`](#debug_get_console)
4. [Mod Tools](#mod-tools)
   - [`mod_list_mods`](#mod_list_mods)
   - [`mod_install_mod`](#mod_install_mod)
   - [`mod_enable_mod`](#mod_enable_mod)

---

## Scene Tools

Tools for querying, creating, and deleting entities in the active scene.

---

### `scene_get_entities`

**Description:**  
Lists all entities currently present in the active scene. Returns entity metadata including unique IDs, display names, and transform data (position, rotation, scale). Useful for inspecting the scene hierarchy and selecting targets for subsequent operations.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `filter` | `string` | No | `""` (empty) | Substring filter to match entity names. Case-insensitive. If empty, all entities are returned. |
| `limit` | `int` | No | `100` | Maximum number of entities to return. Range: `1`–`1000`. |

**Return Format:**

```json
{
  "entities": [
    {
      "id": "ent_3a1f0c",
      "name": "Player",
      "tags": ["player", "character"],
      "position": { "x": 0.0, "y": 1.5, "z": -3.0 },
      "rotation": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
      "scale": { "x": 1.0, "y": 1.0, "z": 1.0 },
      "components": ["TransformComponent", "MeshRendererComponent", "RigidBodyComponent"],
      "parent_id": null,
      "children": ["ent_b2d910"]
    }
  ],
  "total_count": 1,
  "scene_name": "MainScene"
}
```

**Example Usage:**

```
# List all entities
scene_get_entities()

# List entities whose names contain "tree"
scene_get_entities(filter="tree")

# List first 10 entities
scene_get_entities(limit=10)
```

---

### `scene_create_entity`

**Description:**  
Creates a new empty entity in the active scene at the specified position. The entity is assigned a unique ID automatically. Optionally accepts a parent entity to create it as a child in the hierarchy.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `name` | `string` | **Yes** | — | Display name for the new entity. Must be non-empty. |
| `position` | `object` | No | `{"x": 0.0, "y": 0.0, "z": 0.0}` | World-space position as `{x, y, z}` floats. |
| `tags` | `string[]` | No | `[]` | List of string tags to attach to the entity. |
| `parent_id` | `string` | No | `null` | Entity ID of the parent. If `null`, the entity is created at the scene root. |

**Return Format:**

```json
{
  "success": true,
  "entity": {
    "id": "ent_c7e2a4",
    "name": "NewLight",
    "position": { "x": 5.0, "y": 3.0, "z": 0.0 },
    "parent_id": null
  }
}
```

**Example Usage:**

```
# Create entity at origin
scene_create_entity(name="SpawnPoint")

# Create entity at specific position with tags
scene_create_entity(
  name="StreetLamp",
  position={"x": 10.0, "y": 0.0, "z": -5.0},
  tags=["light", "outdoor"]
)

# Create a child entity under an existing parent
scene_create_entity(name="Wheel_FL", parent_id="ent_3a1f0c")
```

---

### `scene_delete_entity`

**Description:**  
Removes an entity and all of its children from the active scene. The operation is permanent and cannot be undone. All attached components are destroyed.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `entity_id` | `string` | **Yes** | — | Unique ID of the entity to delete (e.g., `"ent_3a1f0c"`). |

**Return Format:**

```json
{
  "success": true,
  "deleted_id": "ent_3a1f0c",
  "deleted_children_count": 1
}
```

Returns an error object if the entity ID does not exist:

```json
{
  "success": false,
  "error": "Entity not found: ent_999999"
}
```

**Example Usage:**

```
# Delete a specific entity
scene_delete_entity(entity_id="ent_3a1f0c")
```

---

## Editor Tools

Tools for querying the editor state and capturing the current viewport.

---

### `editor_get_status`

**Description:**  
Returns the current health and operational status of the Modulus Engine editor instance. Reports engine version, uptime, memory usage, scene name, and whether the simulation is running.

**Parameters:**  
*None.*

**Return Format:**

```json
{
  "status": "running",
  "engine_version": "1.4.2",
  "uptime_seconds": 3742,
  "active_scene": "MainScene",
  "simulation_state": "paused",
  "fps": 60.0,
  "memory": {
    "used_mb": 512.3,
    "total_mb": 2048.0
  },
  "platform": "Windows x64",
  "gpu": "NVIDIA GeForce RTX 4070"
}
```

**Status values:** `"running"`, `"degraded"`, `"error"`, `"starting"`.

**Example Usage:**

```
# Check engine health
editor_get_status()
```

---

### `editor_take_screenshot`

**Description:**  
Captures the current viewport of the Modulus Engine editor and returns the image as a base64-encoded PNG. Useful for visual inspection, documentation, or AI-based scene analysis.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `width` | `int` | No | `1920` | Output image width in pixels. |
| `height` | `int` | No | `1080` | Output image height in pixels. |
| `camera` | `string` | No | `"editor"` | Camera source. `"editor"` uses the current editor camera; `"main"` uses the scene's main camera. |
| `include_ui` | `bool` | No | `false` | If `true`, the editor UI overlay is included in the capture. |

**Return Format:**

```json
{
  "success": true,
  "format": "png",
  "width": 1920,
  "height": 1080,
  "data": "<base64-encoded PNG data>",
  "captured_at": "2026-06-09T14:32:01Z"
}
```

**Example Usage:**

```
# Default full-HD screenshot
editor_take_screenshot()

# Custom resolution, include editor UI
editor_take_screenshot(width=1280, height=720, include_ui=true)

# Capture from the scene's main gameplay camera
editor_take_screenshot(camera="main")
```

---

## Debug Tools

Tools for inspecting engine logs and the developer console.

---

### `debug_get_logs`

**Description:**  
Retrieves recent log entries from the Modulus Engine logging system. Supports filtering by severity level. Entries are returned in chronological order (oldest first).

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `level` | `string` | No | `"all"` | Minimum severity filter. One of `"all"`, `"debug"`, `"info"`, `"warning"`, `"error"`, `"fatal"`. Returns entries at or above the specified level. |
| `limit` | `int` | No | `50` | Maximum number of entries to return. Range: `1`–`500`. |
| `since` | `string` | No | `null` | ISO-8601 timestamp. If provided, only entries after this time are returned. |

**Return Format:**

```json
{
  "logs": [
    {
      "timestamp": "2026-06-09T14:30:12.341Z",
      "level": "info",
      "category": "SceneLoader",
      "message": "Loaded scene 'MainScene' with 42 entities."
    },
    {
      "timestamp": "2026-06-09T14:30:13.002Z",
      "level": "warning",
      "category": "Physics",
      "message": "RigidBody on 'ent_b2d910' has zero mass; defaulting to 1.0."
    }
  ],
  "returned_count": 2,
  "total_available": 137
}
```

**Example Usage:**

```
# Get last 50 log entries (all levels)
debug_get_logs()

# Get only errors and fatals, last 100
debug_get_logs(level="error", limit=100)

# Get warnings+ since a specific time
debug_get_logs(level="warning", since="2026-06-09T14:00:00Z")
```

---

### `debug_get_console`

**Description:**  
Returns the raw output from the developer console. This includes direct user input commands, script output, and system messages. Differs from `debug_get_logs` in that it reflects the interactive console buffer, not the structured logging system.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `lines` | `int` | No | `100` | Number of most recent lines to return. Range: `1`–`1000`. |
| `search` | `string` | No | `""` | If non-empty, only lines containing this substring (case-insensitive) are returned. |

**Return Format:**

```json
{
  "console_lines": [
    "> physics.gravity = vec3(0, -9.81, 0)",
    "Gravity set to (0.0, -9.81, 0.0)",
    "> scene.spawn('Enemy', pos=vec3(10, 0, 5))",
    "Spawned entity 'Enemy' (ent_d4f8b1) at (10.0, 0.0, 5.0)",
    "[ScriptError] nil reference in AIController.lua:42"
  ],
  "returned_count": 5
}
```

**Example Usage:**

```
# Get last 100 console lines
debug_get_console()

# Get last 200 lines
debug_get_console(lines=200)

# Search console for error-related output
debug_get_console(search="error")
```

---

## Mod Tools

Tools for managing Modulus Engine mods (`.modpkg` packages).

---

### `mod_list_mods`

**Description:**  
Lists all mods that are currently installed in the Modulus Engine mod directory. Returns each mod's metadata, installation status, and enabled/disabled state.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `enabled_only` | `bool` | No | `false` | If `true`, only currently enabled mods are returned. |

**Return Format:**

```json
{
  "mods": [
    {
      "mod_id": "mod_physics_plus",
      "name": "Physics Plus",
      "version": "2.1.0",
      "author": "ModulusLabs",
      "description": "Enhanced physics with soft-body and fluid simulation.",
      "enabled": true,
      "installed_at": "2026-05-20T09:00:00Z",
      "entry_point": "init.lua",
      "dependencies": ["mod_core_physics"]
    },
    {
      "mod_id": "mod_terrain_gen",
      "name": "Terrain Generator",
      "version": "1.0.3",
      "author": "CommunityDev",
      "description": "Procedural terrain generation toolkit.",
      "enabled": false,
      "installed_at": "2026-06-01T12:30:00Z",
      "entry_point": "init.lua",
      "dependencies": []
    }
  ],
  "total_count": 2,
  "enabled_count": 1
}
```

**Example Usage:**

```
# List all installed mods
mod_list_mods()

# List only enabled mods
mod_list_mods(enabled_only=true)
```

---

### `mod_install_mod`

**Description:**  
Installs a `.modpkg` file into the Modulus Engine mod directory. The package is extracted, validated for manifest integrity, and registered with the engine. If the mod has dependencies that are not installed, the installation fails with a dependency error.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | `string` | **Yes** | — | Absolute or relative file path to the `.modpkg` file to install. |
| `force` | `bool` | No | `false` | If `true`, overwrite an existing installation of the same mod ID without prompting. |

**Return Format:**

```json
{
  "success": true,
  "mod_id": "mod_physics_plus",
  "name": "Physics Plus",
  "version": "2.1.0",
  "installed_path": "C:/Users/jarja/AppData/Local/ModulusEngine/mods/mod_physics_plus/",
  "overwrote_previous": false
}
```

Returns an error object on failure:

```json
{
  "success": false,
  "error": "Missing dependency: mod_core_physics is not installed."
}
```

**Example Usage:**

```
# Install a mod package
mod_install_mod(path="C:/Downloads/physics_plus_v2.1.0.modpkg")

# Force-reinstall over existing version
mod_install_mod(path="C:/Downloads/physics_plus_v2.1.0.modpkg", force=true)
```

---

### `mod_enable_mod`

**Description:**  
Enables or disables an installed mod. When enabled, the mod's entry point script is loaded on the next engine restart or scene reload. Disabling a mod prevents it from loading but does not remove it from disk.

**Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `mod_id` | `string` | **Yes** | — | The unique mod ID to toggle (e.g., `"mod_physics_plus"`). |
| `enabled` | `bool` | No | `true` | `true` to enable the mod; `false` to disable it. |

**Return Format:**

```json
{
  "success": true,
  "mod_id": "mod_physics_plus",
  "enabled": true,
  "message": "Mod enabled. Changes will take effect on next scene reload."
}
```

Returns an error object if the mod ID is not found:

```json
{
  "success": false,
  "error": "Mod not installed: mod_nonexistent"
}
```

**Example Usage:**

```
# Enable a mod
mod_enable_mod(mod_id="mod_physics_plus")

# Explicitly disable a mod
mod_enable_mod(mod_id="mod_terrain_gen", enabled=false)
```

---

## Quick Reference Table

| Tool | Category | Required Params | Returns |
|------|----------|----------------|---------|
| `scene_get_entities` | Scene | — | Entity list with transforms & hierarchy |
| `scene_create_entity` | Scene | `name` | Created entity ID and metadata |
| `scene_delete_entity` | Scene | `entity_id` | Confirmation with child count |
| `editor_get_status` | Editor | — | Engine health, version, memory, FPS |
| `editor_take_screenshot` | Editor | — | Base64-encoded viewport image |
| `debug_get_logs` | Debug | — | Structured log entries by severity |
| `debug_get_console` | Debug | — | Raw console output lines |
| `mod_list_mods` | Mod | — | Installed/enabled mod catalog |
| `mod_install_mod` | Mod | `path` | Installation result with mod metadata |
| `mod_enable_mod` | Mod | `mod_id` | Enable/disable confirmation |

---

## Error Handling

All tools return a consistent error structure when a request fails:

```json
{
  "success": false,
  "error": "<human-readable error description>",
  "error_code": "<MACHINE_READABLE_ERROR_CODE>"
}
```

Common error codes:

| Code | Meaning |
|------|---------|
| `ENTITY_NOT_FOUND` | The specified `entity_id` does not exist in the scene. |
| `SCENE_NOT_LOADED` | No active scene is loaded in the editor. |
| `INVALID_PARAMETER` | A required parameter is missing or a value is out of range. |
| `MOD_NOT_FOUND` | The specified `mod_id` is not installed. |
| `MOD_DEPENDENCY_MISSING` | A required dependency mod is not installed. |
| `MOD_ALREADY_INSTALLED` | The mod is already installed and `force` was not set. |
| `FILE_NOT_FOUND` | The specified file path does not exist. |
| `PERMISSION_DENIED` | The engine does not have permission to access the file or resource. |

---

*End of MCP Tool Reference — Modulus Engine*
