using System.Collections.Concurrent;
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
        var topLevelName = plusIndex >= 0 ? typeName[..plusIndex] : typeName;

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
        var isInterface = (attributes & System.Reflection.TypeAttributes.Interface) != 0;
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

    private record struct Usage(string TypeFullName, string MemberName, string UsageKind);

    public async Task<string> FindUsagesAsync(
        string assemblyPath,
        string typeName,
        string? memberName = null,
        CancellationToken ct = default
    )
    {
        using var module = await Task.Run(() => new PEFile(assemblyPath));
        var metadata = module.Metadata;

        // Step 1: Resolve target handles
        var typeHandle = FindTypeHandle(module, typeName);
        var targetHandles = new HashSet<EntityHandle>();
        string targetDisplayName;

        if (memberName == null)
        {
            targetHandles.Add(typeHandle);
            targetDisplayName = typeName;
        }
        else
        {
            targetDisplayName = $"{typeName}.{memberName}";
            var typeDef = metadata.GetTypeDefinition(typeHandle);

            foreach (var mh in typeDef.GetMethods())
            {
                var m = metadata.GetMethodDefinition(mh);
                if (metadata.GetString(m.Name) == memberName)
                    targetHandles.Add(mh);
            }

            foreach (var fh in typeDef.GetFields())
            {
                var f = metadata.GetFieldDefinition(fh);
                if (metadata.GetString(f.Name) == memberName)
                    targetHandles.Add(fh);
            }

            foreach (var ph in typeDef.GetProperties())
            {
                var p = metadata.GetPropertyDefinition(ph);
                if (metadata.GetString(p.Name) == memberName)
                {
                    var accessors = p.GetAccessors();
                    if (!accessors.Getter.IsNil)
                        targetHandles.Add(accessors.Getter);
                    if (!accessors.Setter.IsNil)
                        targetHandles.Add(accessors.Setter);
                }
            }

            foreach (var eh in typeDef.GetEvents())
            {
                var e = metadata.GetEventDefinition(eh);
                if (metadata.GetString(e.Name) == memberName)
                {
                    var accessors = e.GetAccessors();
                    if (!accessors.Adder.IsNil)
                        targetHandles.Add(accessors.Adder);
                    if (!accessors.Remover.IsNil)
                        targetHandles.Add(accessors.Remover);
                    if (!accessors.Raiser.IsNil)
                        targetHandles.Add(accessors.Raiser);
                }
            }

            if (targetHandles.Count == 0)
                throw new InvalidOperationException(
                    $"Member '{memberName}' not found in type '{typeName}'."
                );
        }

        // Step 2: Scan assembly in parallel
        var usages = await Task.Run(() =>
        {
            var usages = new ConcurrentBag<Usage>();

            Parallel.ForEach(
                metadata.TypeDefinitions,
                new ParallelOptions { CancellationToken = ct },
                scanTypeHandle =>
                {
                    var scanTypeDef = metadata.GetTypeDefinition(scanTypeHandle);
                    var scanTypeName = GetFullTypeName(metadata, scanTypeHandle);

                    // A. Type-level checks (when target is a type)
                    if (memberName == null)
                    {
                        if (
                            !scanTypeDef.BaseType.IsNil
                            && ReferencesTargetType(scanTypeDef.BaseType, targetHandles, metadata)
                        )
                            usages.Add(new Usage(scanTypeName, "", "base type"));

                        foreach (var ifaceImpl in scanTypeDef.GetInterfaceImplementations())
                        {
                            var iface = metadata.GetInterfaceImplementation(ifaceImpl);
                            if (ReferencesTargetType(iface.Interface, targetHandles, metadata))
                                usages.Add(new Usage(scanTypeName, "", "interface implementation"));
                        }
                    }

                    // B. Scan method bodies
                    foreach (var methodHandle in scanTypeDef.GetMethods())
                    {
                        var methodDef = metadata.GetMethodDefinition(methodHandle);
                        if (methodDef.RelativeVirtualAddress == 0)
                            continue;

                        // Don't report self-references from the target's own methods
                        if (targetHandles.Contains(methodHandle))
                            continue;

                        var mName = metadata.GetString(methodDef.Name);

                        try
                        {
                            if (
                                ScanMethodBody(
                                    module,
                                    methodDef,
                                    targetHandles,
                                    typeHandle,
                                    memberName,
                                    metadata
                                )
                            )
                                usages.Add(new Usage(scanTypeName, mName + "()", "method body"));
                        }
                        catch
                        {
                            // Skip methods whose bodies can't be read
                        }
                    }
                }
            );

            return usages;
        });

        // Step 3: Format results
        var sb = new StringBuilder();
        sb.AppendLine($"## Usages of `{targetDisplayName}`");
        sb.AppendLine();

        var grouped = usages.GroupBy(u => u.TypeFullName).OrderBy(g => g.Key).ToList();
        var totalCount = usages.Count;
        var typeCount = grouped.Count;

        if (totalCount == 0)
        {
            sb.AppendLine("No usages found in this assembly.");
        }
        else
        {
            sb.AppendLine(
                $"Found {totalCount} usage{(totalCount != 1 ? "s" : "")} in {typeCount} type{(typeCount != 1 ? "s" : "")}:"
            );
            sb.AppendLine();

            foreach (var group in grouped)
            {
                sb.AppendLine($"### {group.Key}");
                foreach (var usage in group.OrderBy(u => u.MemberName))
                {
                    if (string.IsNullOrEmpty(usage.MemberName))
                        sb.AppendLine($"- {usage.UsageKind}");
                    else
                        sb.AppendLine($"- `{usage.MemberName}` — {usage.UsageKind}");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string GetFullTypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var typeDef = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(typeDef.Name);

        if (!typeDef.IsNested)
        {
            var ns = metadata.GetString(typeDef.Namespace);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

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
        return string.IsNullOrEmpty(outerNs) ? nestedName : $"{outerNs}.{nestedName}";
    }

    private static bool ReferencesTargetType(
        EntityHandle handle,
        HashSet<EntityHandle> targetHandles,
        MetadataReader metadata
    )
    {
        if (handle.IsNil)
            return false;
        if (targetHandles.Contains(handle))
            return true;

        switch (handle.Kind)
        {
            case HandleKind.TypeSpecification:
                // Generic instantiation — check if the root type matches
                var typeSpec = metadata.GetTypeSpecification((TypeSpecificationHandle)handle);
                var sigReader = metadata.GetBlobReader(typeSpec.Signature);
                return SignatureReferencesTargetType(ref sigReader, targetHandles, metadata);

            case HandleKind.MemberReference:
                var memberRef = metadata.GetMemberReference((MemberReferenceHandle)handle);
                return ReferencesTargetType(memberRef.Parent, targetHandles, metadata);

            case HandleKind.MethodSpecification:
                var methodSpec = metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                return ReferencesTargetType(methodSpec.Method, targetHandles, metadata);

            default:
                return false;
        }
    }

    private static bool SignatureReferencesTargetType(
        ref BlobReader reader,
        HashSet<EntityHandle> targetHandles,
        MetadataReader metadata
    )
    {
        if (reader.RemainingBytes == 0)
            return false;

        var typeCode = reader.ReadSignatureTypeCode();
        switch (typeCode)
        {
            case SignatureTypeCode.GenericTypeInstance:
            {
                // Read element type (Class or ValueType)
                var elementType = reader.ReadSignatureTypeCode();
                // Read the generic type def/ref
                var typeHandle = reader.ReadTypeHandle();
                if (targetHandles.Contains(typeHandle))
                    return true;
                // Check generic arguments
                var argCount = reader.ReadCompressedInteger();
                for (var i = 0; i < argCount; i++)
                {
                    if (SignatureReferencesTargetType(ref reader, targetHandles, metadata))
                        return true;
                }
                return false;
            }

            case SignatureTypeCode.SZArray:
            case SignatureTypeCode.Pinned:
            case SignatureTypeCode.ByReference:
            case SignatureTypeCode.Pointer:
                return SignatureReferencesTargetType(ref reader, targetHandles, metadata);

            case SignatureTypeCode.Array:
            {
                var result = SignatureReferencesTargetType(ref reader, targetHandles, metadata);
                // Skip array shape (rank, sizes, lower bounds)
                var rank = reader.ReadCompressedInteger();
                var numSizes = reader.ReadCompressedInteger();
                for (var i = 0; i < numSizes; i++)
                    reader.ReadCompressedInteger();
                var numLoBounds = reader.ReadCompressedInteger();
                for (var i = 0; i < numLoBounds; i++)
                    reader.ReadCompressedSignedInteger();
                return result;
            }

            case SignatureTypeCode.FunctionPointer:
                // Skip function pointer signatures for now
                return false;

            case SignatureTypeCode.TypeHandle:
            {
                var typeHandle = reader.ReadTypeHandle();
                return targetHandles.Contains(typeHandle);
            }

            default:
                // Primitive type or other — not a reference to our target
                return false;
        }
    }

    private static bool ScanMethodBody(
        PEFile module,
        MethodDefinition methodDef,
        HashSet<EntityHandle> targetHandles,
        TypeDefinitionHandle targetTypeHandle,
        string? memberName,
        MetadataReader metadata
    )
    {
        var body = module.Reader.GetMethodBody(methodDef.RelativeVirtualAddress);
        var blob = body.GetILReader();

        while (blob.RemainingBytes > 0)
        {
            var opCode = ReadOpCode(ref blob);
            var operandSize = GetOperandSize(opCode);

            if (operandSize == -1) // Switch
            {
                var count = blob.ReadInt32();
                for (var i = 0; i < count; i++)
                    blob.ReadInt32();
                continue;
            }

            if (operandSize != 4 || !IsTokenOperand(opCode))
            {
                if (operandSize > 0)
                    blob.Offset += operandSize;
                continue;
            }

            // Read the metadata token
            var token = blob.ReadInt32();
            // UserString table (0x70) — skip
            if ((token >> 24) == 0x70)
                continue;

            var handle = MetadataTokens.EntityHandle(token);
            if (handle.IsNil)
                continue;

            if (memberName == null)
            {
                // Searching for type usages
                if (CheckHandleForTypeUsage(handle, targetHandles, targetTypeHandle, metadata))
                    return true;
            }
            else
            {
                // Searching for member usages
                if (
                    CheckHandleForMemberUsage(
                        handle,
                        targetHandles,
                        targetTypeHandle,
                        memberName,
                        metadata
                    )
                )
                    return true;
            }
        }

        return false;
    }

    private static bool CheckHandleForTypeUsage(
        EntityHandle handle,
        HashSet<EntityHandle> targetHandles,
        TypeDefinitionHandle targetTypeHandle,
        MetadataReader metadata
    )
    {
        if (targetHandles.Contains(handle))
            return true;

        switch (handle.Kind)
        {
            case HandleKind.TypeSpecification:
                var typeSpec = metadata.GetTypeSpecification((TypeSpecificationHandle)handle);
                var sigReader = metadata.GetBlobReader(typeSpec.Signature);
                return SignatureReferencesTargetType(ref sigReader, targetHandles, metadata);

            case HandleKind.MethodDefinition:
                var methodDef = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return targetHandles.Contains(methodDef.GetDeclaringType());

            case HandleKind.FieldDefinition:
                var fieldDef = metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                return targetHandles.Contains(fieldDef.GetDeclaringType());

            case HandleKind.MemberReference:
                var memberRef = metadata.GetMemberReference((MemberReferenceHandle)handle);
                return ReferencesTargetType(memberRef.Parent, targetHandles, metadata);

            case HandleKind.MethodSpecification:
                var methodSpec = metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                return CheckHandleForTypeUsage(
                    methodSpec.Method,
                    targetHandles,
                    targetTypeHandle,
                    metadata
                );

            default:
                return false;
        }
    }

    private static bool CheckHandleForMemberUsage(
        EntityHandle handle,
        HashSet<EntityHandle> targetHandles,
        TypeDefinitionHandle targetTypeHandle,
        string memberName,
        MetadataReader metadata
    )
    {
        if (targetHandles.Contains(handle))
            return true;

        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
                var memberRef = metadata.GetMemberReference((MemberReferenceHandle)handle);
                if (metadata.GetString(memberRef.Name) != memberName)
                    return false;
                return ReferencesTargetType(
                    memberRef.Parent,
                    new HashSet<EntityHandle> { targetTypeHandle },
                    metadata
                );

            case HandleKind.MethodSpecification:
                var methodSpec = metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                return CheckHandleForMemberUsage(
                    methodSpec.Method,
                    targetHandles,
                    targetTypeHandle,
                    memberName,
                    metadata
                );

            default:
                return false;
        }
    }

    private static ILOpCode ReadOpCode(ref BlobReader blob)
    {
        byte b = blob.ReadByte();
        return b != 0xFE ? (ILOpCode)b : (ILOpCode)(0xFE00 | blob.ReadByte());
    }

    private static bool IsTokenOperand(ILOpCode opCode)
    {
        return opCode
            is ILOpCode.Ldfld
                or ILOpCode.Ldflda
                or ILOpCode.Stfld
                or ILOpCode.Ldsfld
                or ILOpCode.Ldsflda
                or ILOpCode.Stsfld
                or ILOpCode.Call
                or ILOpCode.Callvirt
                or ILOpCode.Newobj
                or ILOpCode.Ldftn
                or ILOpCode.Ldvirtftn
                or ILOpCode.Jmp
                or ILOpCode.Castclass
                or ILOpCode.Isinst
                or ILOpCode.Newarr
                or ILOpCode.Box
                or ILOpCode.Unbox
                or ILOpCode.Unbox_any
                or ILOpCode.Ldobj
                or ILOpCode.Stobj
                or ILOpCode.Cpobj
                or ILOpCode.Initobj
                or ILOpCode.Sizeof
                or ILOpCode.Ldelem
                or ILOpCode.Stelem
                or ILOpCode.Mkrefany
                or ILOpCode.Refanyval
                or ILOpCode.Constrained
                or ILOpCode.Ldtoken
                or ILOpCode.Calli;
    }

    /// <summary>
    /// Returns the operand size in bytes for an IL opcode,
    /// or -1 for the special-case switch instruction.
    /// </summary>
    private static int GetOperandSize(ILOpCode opCode)
    {
        return opCode switch
        {
            // 8-byte operands
            ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => 8,

            // 4-byte operands (branches, integers, floats, all tokens, strings, sigs)
            ILOpCode.Br
            or ILOpCode.Brfalse
            or ILOpCode.Brtrue
            or ILOpCode.Beq
            or ILOpCode.Bge
            or ILOpCode.Bgt
            or ILOpCode.Ble
            or ILOpCode.Blt
            or ILOpCode.Bne_un
            or ILOpCode.Bge_un
            or ILOpCode.Bgt_un
            or ILOpCode.Ble_un
            or ILOpCode.Blt_un
            or ILOpCode.Leave
            or ILOpCode.Ldc_i4
            or ILOpCode.Ldc_r4
            or ILOpCode.Ldfld
            or ILOpCode.Ldflda
            or ILOpCode.Stfld
            or ILOpCode.Ldsfld
            or ILOpCode.Ldsflda
            or ILOpCode.Stsfld
            or ILOpCode.Call
            or ILOpCode.Callvirt
            or ILOpCode.Newobj
            or ILOpCode.Ldftn
            or ILOpCode.Ldvirtftn
            or ILOpCode.Jmp
            or ILOpCode.Castclass
            or ILOpCode.Isinst
            or ILOpCode.Newarr
            or ILOpCode.Box
            or ILOpCode.Unbox
            or ILOpCode.Unbox_any
            or ILOpCode.Ldobj
            or ILOpCode.Stobj
            or ILOpCode.Cpobj
            or ILOpCode.Initobj
            or ILOpCode.Sizeof
            or ILOpCode.Ldelem
            or ILOpCode.Stelem
            or ILOpCode.Mkrefany
            or ILOpCode.Refanyval
            or ILOpCode.Constrained
            or ILOpCode.Ldtoken
            or ILOpCode.Calli
            or ILOpCode.Ldstr => 4,

            // 2-byte operands (variable/argument index)
            ILOpCode.Ldarg
            or ILOpCode.Ldarga
            or ILOpCode.Starg
            or ILOpCode.Ldloc
            or ILOpCode.Ldloca
            or ILOpCode.Stloc => 2,

            // 1-byte operands (short branches, short integers, short variable index)
            ILOpCode.Br_s
            or ILOpCode.Brfalse_s
            or ILOpCode.Brtrue_s
            or ILOpCode.Beq_s
            or ILOpCode.Bge_s
            or ILOpCode.Bgt_s
            or ILOpCode.Ble_s
            or ILOpCode.Blt_s
            or ILOpCode.Bne_un_s
            or ILOpCode.Bge_un_s
            or ILOpCode.Bgt_un_s
            or ILOpCode.Ble_un_s
            or ILOpCode.Blt_un_s
            or ILOpCode.Leave_s
            or ILOpCode.Ldc_i4_s
            or ILOpCode.Ldarg_s
            or ILOpCode.Ldarga_s
            or ILOpCode.Starg_s
            or ILOpCode.Ldloc_s
            or ILOpCode.Ldloca_s
            or ILOpCode.Stloc_s
            or ILOpCode.Unaligned => 1,

            // Switch: variable length (special handling)
            ILOpCode.Switch => -1,

            // Everything else: no operand
            _ => 0,
        };
    }
}
