using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class AssemblyAttributesTool
{
    [McpServerTool(Name = "get_assembly_attributes", ReadOnly = true, Title = "Get Assembly Attributes")]
    [Description(
        "Decompile assembly-level and module-level attributes from a .NET assembly (e.g. TargetFramework, AssemblyVersion, InternalsVisibleTo)."
    )]
    public static async Task<CallToolResult> GetAssemblyAttributes(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        CancellationToken ct = default
    )
    {
        try
        {
            var source = await ilspy.GetAssemblyAttributesAsync(assemblyPath, ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = source }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Error reading assembly attributes: {ex.Message}" }],
            };
        }
    }
}
