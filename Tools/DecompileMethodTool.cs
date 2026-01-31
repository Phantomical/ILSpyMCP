using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class DecompileMethodTool
{
    [McpServerTool(
        Name = "decompile_method",
        ReadOnly = true,
        Title = "Decompile Method to C#"
    )]
    [Description(
        "Decompile a specific method from a .NET assembly to C# source code. If multiple overloads exist, all are returned."
    )]
    public static async Task<CallToolResult> DecompileMethod(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Fully qualified type name (e.g. Namespace.ClassName)")] string typeName,
        [Description("Method name (e.g. ToString)")] string methodName,
        CancellationToken ct = default
    )
    {
        try
        {
            var source = await ilspy.DecompileMethodAsync(assemblyPath, typeName, methodName, ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = source }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Error decompiling method: {ex.Message}" }],
            };
        }
    }
}
