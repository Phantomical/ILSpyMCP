# Changelog

## 0.2.0

### Added
- `get_assembly_attributes` tool for dumping assembly-level and module-level attributes
- `find_usages` tool for finding references to types and members via IL scanning
- `decompile_method` and `disassemble_method` tools for inspecting individual methods

### Changed
- `decompile_type` now shows only signatures (fields, properties, method signatures, events) instead of full bodies
- Renamed `decompile`/`disassemble` to `decompile_type`/`disassemble_type`
- Removed markdown wrapping from decompile and disassemble output
- Return errors as `CallToolResult` with `IsError` instead of throwing exceptions

### Removed
- `list_members` tool, now redundant with signature-only `decompile_type`

## 0.1.0

### Added
- Initial MCP server with `list_types`, `list_members`, `decompile`, and `disassemble` tools
- Direct ICSharpCode.Decompiler library integration (no ilspycmd dependency)
- Packable as a dotnet tool (`ilspy-mcp`)
- MIT license
- README with usage docs and MCP client config examples
