using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class FindUsagesTool
{
    [McpServerTool(Name = "find_usages", ReadOnly = true, Title = "Find Usages")]
    [Description(
        "Find all usages of a type or member within a .NET assembly. "
            + "If memberName is omitted, finds usages of the type itself (base types, interface implementations, IL references). "
            + "If memberName is provided, finds usages of that method, field, property, or event."
    )]
    public static async Task<CallToolResult> FindUsages(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Fully qualified type name (e.g. Namespace.ClassName)")] string typeName,
        [Description("Optional member name (method, field, property, or event) to find usages of")]
            string? memberName = null,
        CancellationToken ct = default
    )
    {
        try
        {
            var result = await ilspy.FindUsagesAsync(assemblyPath, typeName, memberName, ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = result }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Error finding usages: {ex.Message}" }],
            };
        }
    }
}
