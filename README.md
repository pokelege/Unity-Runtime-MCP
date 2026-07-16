# UnityRuntimeMCP

UnityRuntimeMCP is an implementation of the Model Context Protocol (MCP) as a BepInEx 6 plugin for Unity IL2CPP games. It enables external AI agents to interact with the game runtime via reflection-based tools.

## Features

- **Embedded Web UI Explorer**: Hosts a sleek, responsive browser interface (HTML/CSS/JS) directly at `http://localhost:<Port>/` (supports Auto Light/Dark mode). Allows scene hierarchy tree traversal, inline field editing, static/instance method calling, and live game screenshot stream.
- **Live Inspection & Object Caching**: Discovery and inspection of Unity `GameObject`/`Component` instances, as well as non-Unity reference types (classes and arrays).
- **Pointer-Based Identity Mapping**: Resolves IL2CPP managed wrapper identity shifts by mapping dynamic IDs to native C++ object addresses (`IntPtr`). Keeps active views alive via a 200-object MRU keep-alive cache.
- **Static Member Support**: Full reflection access to read/write static fields/properties, invoke static methods, and inspect static class members without needing instances.
- **Deep Reflection & Serialization**: Access to fields, properties, and methods on derived types in IL2CPP environments. Automatically serializes collections and arrays up to 20 elements, caching nested non-Unity objects for further inspection.
- **Runtime Modification**: Reading and writing values to object fields and properties with support for nested dot-notation paths.
- **Visual Feedback**: Capture of game view screenshots in Base64 PNG format or saved to a cross-platform temporary directory.
- **Thread Safety**: Automatic dispatching of Unity API calls to the main thread.
- **Lifecycle Management**: Protection against scene cleanup and IL2CPP-specific object destruction.

## Provided Tools

The server registers the following MCP tools:

| Tool | Description |
| :--- | :--- |
| `find_objects` | Finds active GameObjects or memory assets/inactive objects (when `include_assets` is true) by class name. |
| `find_types` | Searches loaded system types in the AppDomain. Returns matching full class names. |
| `get_hierarchy` | Returns immediate parent and children IDs for Unity GameObjects. Can return scene roots if `instance_id` is omitted. |
| `inspect_object` | Detailed member view. Supports Unity objects, cached non-Unity objects, or static classes (when `class_name` is specified). |
| `read_field` | Reads a value from an instance field/property, or static field/property if `class_name` is specified. Supports dot-notation paths. |
| `write_field` | Writes a value. Supports nested paths and static fields/properties. |
| `invoke_method` | Executes an instance or static method. Supports generic methods via `type_args`. |
| `take_screenshot` | Captures the game screen. Supports `scale` (0.1 to 1.0) and `save_to_file`. |

## Feature Highlights

- **Embedded Client Routing**: The HttpListener handles standard GET routes (`/`, `/app.js`, `/style.css`, `/assets/*`) to resolve embedded resources, allowing browser-based remote exploration.
- **Identity Mapping & Cache**: The server maintains a weak-reference cache of all encountered Unity Objects and non-Unity reference types (assigned dynamic IDs >= 1,000,000,000). By referencing native IL2CPP memory pointers, IDs remain stable even when wrappers are garbage collected.
- **Robust Path Traversal**: Dot-notation support in `read_field` and `write_field` allows for deep inspection in a single call. The system handles null segments gracefully, returning standard JSON `null` instead of errors.
- **Generic Method Resolution**: Agents can invoke generic Unity methods by providing `type_args` (a list of full type names), enabling advanced operations like `GetComponent<TextMeshProUGUI>()`.
- **Custom Screenshot Engine**: Avoids native IL2CPP `AccessViolationException` crashes by bypassing the native `EncodeToJPG` delegate. Instead, it blits the native screen buffer to a scaled `RenderTexture` and encodes it to a PNG. Scaled output reduces payload size, and setting `save_to_file` saves directly to the system temp directory (`C:\Users\Public\UnityRuntimeMCP_Temp` on Windows, `/tmp/UnityRuntimeMCP_Temp` on macOS/Linux).

## Installation

1. Install BepInEx 6 (IL2CPP) in the target game.
2. Place `UnityRuntimeMCP.dll` in the `BepInEx/plugins` directory.
3. Launch the game; the MCP server initializes on the configured port.

### Configuration
Configuration is managed via `BepInEx/config/me.pokelege.unityruntimemcp.cfg`:
- `Port`: Server listening port (Default: 3000).
- `Host`: Server binding address (Default: 127.0.0.1).

## Building

If you are not using a pre-built release, you can build the plugin from source:

### Prerequisites
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- Unity game assemblies placed in the `libs/` directory (these are already included in the repository for reference).

### Build Commands
To build the project, run one of the following commands in the project root:

- **Debug Build**:
  ```powershell
  dotnet build
  ```
  The compiled DLL will be located at `bin/Debug/net6.0/UnityRuntimeMCP.dll`.

- **Release Build**:
  ```powershell
  dotnet build -c Release
  ```
  The compiled DLL will be located at `bin/Release/net6.0/UnityRuntimeMCP.dll`.

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
