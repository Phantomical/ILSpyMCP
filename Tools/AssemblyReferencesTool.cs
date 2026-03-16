using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class AssemblyReferencesTool
{
    [McpServerTool(
        Name = "list_assembly_references",
        ReadOnly = true,
        Title = "List Assembly References"
    )]
    [Description(
        "List all assemblies referenced by a .NET assembly, including version and public key token."
    )]
    public static async Task<CallToolResult> ListAssemblyReferences(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        CancellationToken ct = default
    )
    {
        try
        {
            var result = await ilspy.GetAssemblyReferencesAsync(assemblyPath, ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = result }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content =
                [
                    new TextContentBlock
                    {
                        Text = $"Error listing assembly references: {ex.Message}",
                    },
                ],
            };
        }
    }
}
