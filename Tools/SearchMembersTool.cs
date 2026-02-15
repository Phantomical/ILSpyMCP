using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class SearchMembersTool
{
    [McpServerTool(Name = "search_members", ReadOnly = true, Title = "Search Type Members")]
    [Description(
        "Search for members (fields, properties, methods, events, nested types) matching a regex pattern on a type. Useful for large types where decompiling the full type would be too large."
    )]
    public static async Task<CallToolResult> SearchMembers(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Fully qualified type name (e.g. Namespace.ClassName)")] string typeName,
        [Description("Regex pattern to filter member names (case-insensitive)")] string pattern,
        CancellationToken ct = default
    )
    {
        try
        {
            var result = await ilspy.SearchMembersAsync(assemblyPath, typeName, pattern, ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = result }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content =
                [
                    new TextContentBlock { Text = $"Error searching members: {ex.Message}" },
                ],
            };
        }
    }
}
