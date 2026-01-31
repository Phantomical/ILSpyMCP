using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class ListTypesTool
{
    [McpServerTool(Name = "list_types", ReadOnly = true, Title = "List Types in Assembly")]
    [Description(
        "List all types (classes, interfaces, structs, delegates, enums) in a .NET assembly. Optionally filter by regex pattern."
    )]
    public static async Task<string> ListTypes(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Optional regex pattern to filter type names")] string? pattern = null,
        CancellationToken ct = default
    )
    {
        var types = await ilspy.ListTypesAsync(assemblyPath, pattern, ct);

        if (types.Count == 0)
            return "No types found.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {types.Count} types:");
        sb.AppendLine();

        var byNamespace = types.GroupBy(t => t.Namespace ?? "(Global)").OrderBy(g => g.Key);

        foreach (var group in byNamespace)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            foreach (var t in group.OrderBy(t => t.Name))
                sb.AppendLine($"- {t.Kind}: `{t.FullName}`");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
