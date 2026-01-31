# ILSpy MCP Server

An [MCP](https://modelcontextprotocol.io/) server that uses [ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy) to provide .NET assembly inspection tools. Lets LLMs list types, decompile C#, and view IL disassembly from compiled assemblies.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)

## Build

```
dotnet build
```

## Usage

The server communicates over stdio using the MCP protocol.

```
dotnet run [options]
```

### Options

| Option | Description |
|---|---|
| `--reference-path <path>` | Add assembly reference directory (repeatable) |
| `-v`, `--verbose` | Enable debug-level logging to stderr |

## Tools

### `list_types`

List all types (classes, interfaces, structs, delegates, enums) in a .NET assembly, grouped by namespace.

| Parameter | Required | Description |
|---|---|---|
| `assemblyPath` | yes | Path to the .dll or .exe |
| `pattern` | no | Regex to filter type names |

### `list_members`

Decompile a type to C# and list its members and nested types.

| Parameter | Required | Description |
|---|---|---|
| `assemblyPath` | yes | Path to the .dll or .exe |
| `typeName` | yes | Fully qualified type name (e.g. `Namespace.ClassName`) |

### `decompile`

Decompile a type to C# source code.

| Parameter | Required | Description |
|---|---|---|
| `assemblyPath` | yes | Path to the .dll or .exe |
| `typeName` | yes | Fully qualified type name |

### `disassemble`

Disassemble a type to IL (Intermediate Language) code.

| Parameter | Required | Description |
|---|---|---|
| `assemblyPath` | yes | Path to the .dll or .exe |
| `typeName` | yes | Fully qualified type name |

## MCP Client Configuration

### Claude Code

Add to your MCP settings:

```json
{
  "mcpServers": {
    "ilspy": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/ILSpyMCP"]
    }
  }
}
```

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "ilspy": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/ILSpyMCP"]
    }
  }
}
```

To pass additional options (e.g. reference paths for framework assemblies):

```json
{
  "mcpServers": {
    "ilspy": {
      "command": "dotnet",
      "args": [
        "run", "--project", "/path/to/ILSpyMCP",
        "--",
        "--reference-path", "/path/to/framework/assemblies"
      ]
    }
  }
}
```
