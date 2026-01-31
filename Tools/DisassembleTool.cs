using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class DisassembleTool
{
    [McpServerTool(Name = "disassemble", ReadOnly = true, Title = "Disassemble Type to IL")]
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

            var sb = new StringBuilder();
            sb.AppendLine($"## IL Disassembly: `{typeName}`");
            sb.AppendLine();
            sb.AppendLine("```il");
            sb.AppendLine(il.TrimEnd());
            sb.AppendLine("```");
            return new CallToolResult { Content = [new TextContentBlock { Text = sb.ToString() }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Error disassembling type: {ex.Message}" }],
            };
        }
    }
}
