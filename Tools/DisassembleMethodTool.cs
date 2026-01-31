using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class DisassembleMethodTool
{
    [McpServerTool(
        Name = "disassemble_method",
        ReadOnly = true,
        Title = "Disassemble Method to IL"
    )]
    [Description(
        "Disassemble a specific method from a .NET assembly to IL (Intermediate Language) code. If multiple overloads exist, all are returned."
    )]
    public static async Task<CallToolResult> DisassembleMethod(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Fully qualified type name (e.g. Namespace.ClassName)")] string typeName,
        [Description("Method name (e.g. ToString)")] string methodName,
        CancellationToken ct = default
    )
    {
        try
        {
            var il = await ilspy.DisassembleMethodAsync(assemblyPath, typeName, methodName, ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = il }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Error disassembling method: {ex.Message}" }],
            };
        }
    }
}
