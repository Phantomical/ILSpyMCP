using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ILSpyMCP.Tools;

[McpServerToolType]
public sealed class DecompileTool
{
    [McpServerTool(Name = "decompile", ReadOnly = true, Title = "Decompile Type to C#")]
    [Description("Decompile a type from a .NET assembly to C# source code.")]
    public static async Task<CallToolResult> Decompile(
        ILSpyService ilspy,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Fully qualified type name (e.g. Namespace.ClassName)")] string typeName,
        CancellationToken ct = default
    )
    {
        try
        {
            var source = await ilspy.DecompileAsync(assemblyPath, typeName, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"## Decompiled: `{typeName}`");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            sb.AppendLine(source.TrimEnd());
            sb.AppendLine("```");
            return new CallToolResult { Content = [new TextContentBlock { Text = sb.ToString() }] };
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
