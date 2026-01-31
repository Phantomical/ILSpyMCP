using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace ILSpyMCP;

public sealed class ILSpyServiceOptions
{
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
    private readonly List<string> _referencePaths;

    public ILSpyService(ILSpyServiceOptions options)
    {
        _referencePaths = options.ReferencePaths;
    }

    private CSharpDecompiler CreateDecompiler(string assemblyPath)
    {
        var module = new PEFile(assemblyPath);
        var resolver = new UniversalAssemblyResolver(
            assemblyPath,
            throwOnError: false,
            module.DetectTargetFrameworkId()
        );
        foreach (var refPath in _referencePaths)
            resolver.AddSearchDirectory(refPath);
        return new CSharpDecompiler(module, resolver, new DecompilerSettings());
    }

    private static TypeDefinitionHandle FindTypeHandle(PEFile module, string typeName)
    {
        // Handle nested types: Namespace.Outer+Inner
        var plusIndex = typeName.IndexOf('+');
        var topLevelName =
            plusIndex >= 0 ? typeName[..plusIndex] : typeName;

        var handle = module.GetTypeDefinition(new TopLevelTypeName(topLevelName));
        if (handle.IsNil)
            throw new InvalidOperationException($"Type '{typeName}' not found in assembly.");

        if (plusIndex < 0)
            return handle;

        // Walk nested types
        var nestedPath = typeName[(plusIndex + 1)..].Split('+');
        var metadata = module.Metadata;
        foreach (var nestedName in nestedPath)
        {
            var typeDef = metadata.GetTypeDefinition(handle);
            var found = false;
            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                var nested = metadata.GetTypeDefinition(nestedHandle);
                if (metadata.GetString(nested.Name) == nestedName)
                {
                    handle = nestedHandle;
                    found = true;
                    break;
                }
            }
            if (!found)
                throw new InvalidOperationException(
                    $"Nested type '{nestedName}' not found in '{typeName}'."
                );
        }
        return handle;
    }

    private static string GetTypeKind(MetadataReader metadata, TypeDefinition typeDef)
    {
        var attributes = typeDef.Attributes;
        var isInterface =
            (attributes & System.Reflection.TypeAttributes.Interface) != 0;
        if (isInterface)
            return "Interface";

        var baseTypeHandle = typeDef.BaseType;
        if (!baseTypeHandle.IsNil)
        {
            var baseTypeName = baseTypeHandle.Kind switch
            {
                HandleKind.TypeReference => metadata
                    .GetTypeReference((TypeReferenceHandle)baseTypeHandle)
                    .Name,
                HandleKind.TypeDefinition => metadata
                    .GetTypeDefinition((TypeDefinitionHandle)baseTypeHandle)
                    .Name,
                _ => default,
            };
            if (!baseTypeName.IsNil)
            {
                var name = metadata.GetString(baseTypeName);
                if (name == "Enum")
                    return "Enum";
                if (name == "ValueType")
                    return "Struct";
                if (name == "MulticastDelegate" || name == "Delegate")
                    return "Delegate";
            }
        }
        return "Class";
    }

    public Task<List<TypeInfo>> ListTypesAsync(
        string assemblyPath,
        string? pattern = null,
        CancellationToken ct = default
    )
    {
        using var module = new PEFile(assemblyPath);
        var metadata = module.Metadata;
        Regex? filterRegex = pattern != null ? new Regex(pattern, RegexOptions.IgnoreCase) : null;

        var types = new List<TypeInfo>();

        foreach (var handle in metadata.TypeDefinitions)
        {
            ct.ThrowIfCancellationRequested();
            var typeDef = metadata.GetTypeDefinition(handle);

            // Skip the <Module> type
            var name = metadata.GetString(typeDef.Name);
            if (name == "<Module>")
                continue;

            var ns = metadata.GetString(typeDef.Namespace);
            var isNested = typeDef.IsNested;

            // Build full name
            string fullName;
            if (isNested)
            {
                // Build nested type name with + separator
                var parts = new List<string> { name };
                var current = typeDef;
                while (current.IsNested)
                {
                    var declaringHandle = current.GetDeclaringType();
                    current = metadata.GetTypeDefinition(declaringHandle);
                    parts.Add(metadata.GetString(current.Name));
                }
                parts.Reverse();
                var outerNs = metadata.GetString(current.Namespace);
                var nestedName = string.Join("+", parts);
                fullName = string.IsNullOrEmpty(outerNs) ? nestedName : $"{outerNs}.{nestedName}";
            }
            else
            {
                fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }

            if (filterRegex != null && !filterRegex.IsMatch(fullName))
                continue;

            var kind = GetTypeKind(metadata, typeDef);

            // For display, namespace is the part before the last dot
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

        return Task.FromResult(types);
    }

    public Task<string> DecompileAsync(
        string assemblyPath,
        string typeName,
        CancellationToken ct = default
    )
    {
        var decompiler = CreateDecompiler(assemblyPath);
        var result = decompiler.DecompileTypeAsString(new FullTypeName(typeName));
        return Task.FromResult(result);
    }

    public Task<string> DisassembleAsync(
        string assemblyPath,
        string typeName,
        CancellationToken ct = default
    )
    {
        using var module = new PEFile(assemblyPath);
        var handle = FindTypeHandle(module, typeName);
        var writer = new StringWriter();
        var output = new PlainTextOutput(writer);
        var disassembler = new ReflectionDisassembler(output, ct);
        disassembler.DisassembleType(module, handle);
        return Task.FromResult(writer.ToString());
    }

    public Task<string> DecompileMethodAsync(
        string assemblyPath,
        string typeName,
        string methodName,
        CancellationToken ct = default
    )
    {
        var decompiler = CreateDecompiler(assemblyPath);
        var fullTypeName = new FullTypeName(typeName);
        var typeHandle = decompiler.TypeSystem.FindType(fullTypeName).GetDefinition();
        if (typeHandle == null)
            throw new InvalidOperationException($"Type '{typeName}' not found in assembly.");

        var matches = typeHandle.Methods.Where(m => m.Name == methodName).ToList();
        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"Method '{methodName}' not found in type '{typeName}'."
            );

        var sb = new StringBuilder();
        for (var i = 0; i < matches.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }
            var handle = (MethodDefinitionHandle)matches[i].MetadataToken;
            sb.Append(decompiler.DecompileAsString(handle).TrimEnd());
        }

        return Task.FromResult(sb.ToString());
    }

    public Task<string> DisassembleMethodAsync(
        string assemblyPath,
        string typeName,
        string methodName,
        CancellationToken ct = default
    )
    {
        using var module = new PEFile(assemblyPath);
        var typeHandle = FindTypeHandle(module, typeName);
        var metadata = module.Metadata;
        var typeDef = metadata.GetTypeDefinition(typeHandle);

        var matches = new List<MethodDefinitionHandle>();
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            if (metadata.GetString(method.Name) == methodName)
                matches.Add(methodHandle);
        }

        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"Method '{methodName}' not found in type '{typeName}'."
            );

        var writer = new StringWriter();
        var output = new PlainTextOutput(writer);
        var disassembler = new ReflectionDisassembler(output, ct);

        for (var i = 0; i < matches.Count; i++)
        {
            if (i > 0)
                writer.WriteLine();
            disassembler.DisassembleMethod(module, matches[i]);
        }

        return Task.FromResult(writer.ToString());
    }

    public async Task<string> ListMembersAsync(
        string assemblyPath,
        string typeName,
        CancellationToken ct = default
    )
    {
        var decompiled = await DecompileAsync(assemblyPath, typeName, ct);

        // Find nested types using metadata
        using var module = new PEFile(assemblyPath);
        var handle = FindTypeHandle(module, typeName);
        var metadata = module.Metadata;
        var typeDef = metadata.GetTypeDefinition(handle);
        var nestedTypes = new List<(string Kind, string Name)>();
        foreach (var nestedHandle in typeDef.GetNestedTypes())
        {
            var nested = metadata.GetTypeDefinition(nestedHandle);
            var nestedName = metadata.GetString(nested.Name);
            var kind = GetTypeKind(metadata, nested);
            nestedTypes.Add((kind, $"{typeName}+{nestedName}"));
        }

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
            foreach (var (kind, name) in nestedTypes)
                sb.AppendLine($"- {kind}: `{name}`");
        }

        return sb.ToString();
    }
}
