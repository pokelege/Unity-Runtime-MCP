# UnityRuntimeMCP

UnityRuntimeMCP is an implementation of the Model Context Protocol (MCP) as a BepInEx 6 plugin for Unity IL2CPP games. It enables external AI agents to interact with the game runtime via reflection-based tools.

## Features

- **Live Inspection**: Discovery and inspection of Unity GameObject and Component instances in the active scene.
- **Deep Reflection**: Access to fields, properties, and methods on derived types in IL2CPP environments.
- **Runtime Modification**: Reading and writing values to object fields and properties.
- **Visual Feedback**: Capture of game view screenshots in Base64 PNG format.
- **Thread Safety**: Automatic dispatching of Unity API calls to the main thread.
- **Lifecycle Management**: Protection against scene cleanup and IL2CPP-specific object destruction.

## Provided Tools

The server registers the following MCP tools:

| Tool | Description |
| :--- | :--- |
| `find_objects` | Finds active GameObjects by class name. |
| `inspect_object` | Returns a detailed view of object members by Instance ID. |
| `read_field` | Reads the value of a specific field or property. |
| `write_field` | Writes a value to a field or property with type conversion. |
| `invoke_method` | Executes a method on an object with parameter support. |
| `take_screenshot` | Captures the game screen as a Base64 PNG. |

## Installation

1. Install BepInEx 6 (IL2CPP) in the target game.
2. Place `UnityRuntimeMCP.dll` in the `BepInEx/plugins` directory.
3. Launch the game; the MCP server initializes on the configured port.

### Configuration
Configuration is managed via `BepInEx/config/me.pokelege.unityruntimemcp.cfg`:
- `Port`: Server listening port (Default: 3000).
- `Host`: Server binding address (Default: 127.0.0.1).

## Connection

The server utilizes HTTP/SSE transport.

### Client Configuration
MCP clients should connect to the following endpoint:
`http://127.0.0.1:3000/mcp`

### Technical Specifications
- **Protocol**: JSON-RPC 2.0 over Server-Sent Events (SSE).
- **Initialization**: Requires standard MCP initialize handshake.
- **Session Management**: Supports request ID tracking for JSON-RPC compliance.

## Technical Architecture

- **Main Thread Dispatcher**: A persistent MonoBehaviour managing a thread-safe execution queue.
- **IL2CPP Bridge**: Custom type resolution mapping IL2CPP internal types to managed System.Type for reflection accuracy.
- **Modular Design**: Tool implementations are decoupled for extensibility.

## Prohibited Use

This software is provided for research and development purposes. Use of this software to facilitate cheating in multiplayer or online games is strictly prohibited. Users assume all risk regarding detection by anti-cheat systems and subsequent account actions.

## License

This project is licensed under the GNU Lesser General Public License v2.1. See the [LICENSE](LICENSE) file for the full license text and disclaimer.
