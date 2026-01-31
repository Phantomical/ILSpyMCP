using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ILSpyMCP;

public sealed class ILSpyServiceOptions
{
    public string? IlspyCmdPath { get; set; }
    public List<string> ReferencePaths { get; set; } = [];
}

public sealed class TypeInfo
{
    public required string Kind { get; init; }
    public required string FullName { get; init; }
    public string? Namespace { get; init; }
    public required string Name { get; init; }
}

public sealed class ILSpyService
{
    private readonly string _ilspyCmdPath;
    private readonly List<string> _referencePaths;

    public ILSpyService(ILSpyServiceOptions options)
    {
        _ilspyCmdPath =
            options.IlspyCmdPath
            ?? FindIlspyCmd()
            ?? throw new InvalidOperationException(
                "ilspycmd not found. Install with: dotnet tool install --global ilspycmd"
            );
        _referencePaths = options.ReferencePaths;
    }

    private static string? FindIlspyCmd()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", "" } : new[] { "" };

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, "ilspycmd" + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        List<string> args,
        CancellationToken ct = default
    )
    {
        // Always add reference paths and disable update check
        foreach (var refPath in _referencePaths)
        {
            args.Add("-r");
            args.Add(refPath);
        }
        args.Add("--disable-updatecheck");

        var psi = new ProcessStartInfo
        {
            FileName = _ilspyCmdPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ilspycmd");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        await proc.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (proc.ExitCode, stdout, stderr);
    }

    public async Task<List<TypeInfo>> ListTypesAsync(
        string assemblyPath,
        string? pattern = null,
        CancellationToken ct = default
    )
    {
        var args = new List<string> { assemblyPath, "-l", "cisde" };
        var (exitCode, stdout, stderr) = await RunAsync(args, ct);

        if (exitCode != 0)
            throw new InvalidOperationException($"ilspycmd failed: {stderr}");

        var regex = new Regex(@"^(\w+)\s+(.+)$", RegexOptions.Multiline);
        Regex? filterRegex = pattern != null ? new Regex(pattern, RegexOptions.IgnoreCase) : null;

        var types = new List<TypeInfo>();
        foreach (Match m in regex.Matches(stdout))
        {
            var kind = m.Groups[1].Value;
            var fullName = m.Groups[2].Value.Trim();

            if (filterRegex != null && !filterRegex.IsMatch(fullName))
                continue;

            var lastDot = fullName.LastIndexOf('.');
            types.Add(
                new TypeInfo
                {
                    Kind = kind,
                    FullName = fullName,
                    Namespace = lastDot >= 0 ? fullName[..lastDot] : null,
                    Name = lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName,
                }
            );
        }
        return types;
    }

    public async Task<string> DecompileAsync(
        string assemblyPath,
        string typeName,
        CancellationToken ct = default
    )
    {
        var args = new List<string> { assemblyPath, "-t", typeName };
        var (exitCode, stdout, stderr) = await RunAsync(args, ct);

        if (exitCode != 0)
            throw new InvalidOperationException($"ilspycmd failed: {stderr}");

        return stdout;
    }

    public async Task<string> DisassembleAsync(
        string assemblyPath,
        string typeName,
        CancellationToken ct = default
    )
    {
        var args = new List<string> { assemblyPath, "-t", typeName, "-il" };
        var (exitCode, stdout, stderr) = await RunAsync(args, ct);

        if (exitCode != 0)
            throw new InvalidOperationException($"ilspycmd failed: {stderr}");

        return stdout;
    }

    public async Task<string> ListMembersAsync(
        string assemblyPath,
        string typeName,
        CancellationToken ct = default
    )
    {
        // Decompile the type to C# to get member signatures
        var decompiled = await DecompileAsync(assemblyPath, typeName, ct);

        // Also find nested types
        var allTypes = await ListTypesAsync(assemblyPath, null, ct);
        var nestedPrefix = typeName + "+";
        var nestedTypes = allTypes.Where(t => t.FullName.StartsWith(nestedPrefix)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"## Members of `{typeName}`");
        sb.AppendLine();
        sb.AppendLine("### Decompiled Source");
        sb.AppendLine("```csharp");
        sb.AppendLine(decompiled.TrimEnd());
        sb.AppendLine("```");

        if (nestedTypes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Nested Types");
            foreach (var nt in nestedTypes)
                sb.AppendLine($"- {nt.Kind}: `{nt.FullName}`");
        }

        return sb.ToString();
    }
}
