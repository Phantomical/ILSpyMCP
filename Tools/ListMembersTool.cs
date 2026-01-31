using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class ListMembersTool
{
    [McpServerTool(Name = "list_members", ReadOnly = true, Title = "List Members of a Type")]
    [Description(
        "List members (methods, fields, properties, nested types) of a specific type by decompiling it to C#."
    )]
    public static async Task<string> ListMembers(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Fully qualified type name (e.g. Namespace.ClassName)")] string typeName,
        CancellationToken ct = default
    )
    {
        return await ilspy.ListMembersAsync(assemblyPath, typeName, ct);
    }
}
