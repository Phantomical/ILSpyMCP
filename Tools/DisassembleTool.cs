using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class DisassembleTool
{
    [McpServerTool(Name = "disassemble_type", ReadOnly = true, Title = "Disassemble Type to IL")]
    [Description("Disassemble a type from a .NET assembly to IL (Intermediate Language) code.")]
    public static async Task<CallToolResult> Disassemble(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Fully qualified type name (e.g. Namespace.ClassName)")] string typeName,
        CancellationToken ct = default
    )
    {
        try
        {
            var il = await ilspy.DisassembleAsync(assemblyPath, typeName, ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = il }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content =
                [
                    new TextContentBlock { Text = $"Error disassembling type: {ex.Message}" },
                ],
            };
        }
    }
}
