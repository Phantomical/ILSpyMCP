using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class DecompileTool
{
    [McpServerTool(Name = "decompile_type", ReadOnly = true, Title = "Decompile Type to C#")]
    [Description("Decompile a type from a .NET assembly showing only signatures (fields, property/method/event signatures, no method bodies). Use decompile_method to get the full body of a specific method.")]
    public static async Task<CallToolResult> Decompile(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Fully qualified type name (e.g. Namespace.ClassName)")] string typeName,
        CancellationToken ct = default
    )
    {
        try
        {
            var source = await ilspy.DecompileTypeSignaturesAsync(assemblyPath, typeName, ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = source }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Error decompiling type: {ex.Message}" }],
            };
        }
    }
}
