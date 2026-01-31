using System.CommandLine;
using System.CommandLine.Parsing;
using ILSpyMCP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var ilspyCmdPathOption = new Option<string?>(
    "--ilspycmd-path",
    "Path to ilspycmd executable (default: auto-detect from PATH)"
);

var referencePathOption = new Option<string[]>(
    "--reference-path",
    "Add default assembly reference directory (repeatable)"
);

var verboseOption = new Option<bool>("--verbose", "Enable debug-level logging to stderr");
verboseOption.Aliases.Add("-v");

var rootCommand = new RootCommand("ILSpy MCP Server - inspect .NET assemblies via MCP tools")
{
    ilspyCmdPathOption,
    referencePathOption,
    verboseOption,
};

rootCommand.SetAction(
    async (parseResult, ct) =>
    {
        var ilspyPath = parseResult.GetValue(ilspyCmdPathOption);
        var refPaths = parseResult.GetValue(referencePathOption) ?? [];
        var verbose = parseResult.GetValue(verboseOption);

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);

        builder.Services.AddSingleton(
            new ILSpyServiceOptions { IlspyCmdPath = ilspyPath, ReferencePaths = refPaths.ToList() }
        );
        builder.Services.AddSingleton<ILSpyService>();

        builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();

        await builder.Build().RunAsync(ct);
    }
);

var parseResult = CommandLineParser.Parse(rootCommand, args);
await parseResult.InvokeAsync();
