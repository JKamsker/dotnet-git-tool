using System.Text.RegularExpressions;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.State;

namespace DotnetGitTool.Workflows;

public enum ToolCommandStyle
{
    Dotnet,
    Standalone,
}

public sealed partial record ToolCommandIdentity(
    string BaseName,
    ToolCommandStyle Style,
    string PackageCommand,
    string Invocation)
{
    public string StyleName => Style == ToolCommandStyle.Dotnet ? "dotnet" : "standalone";

    public static ToolCommandIdentity Create(string discoveredCommand, ToolCommandStyle style)
    {
        var command = discoveredCommand.Trim();
        var baseName = command.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase)
            ? command["dotnet-".Length..]
            : command;
        if (string.IsNullOrWhiteSpace(baseName) || !ValidCommandName().IsMatch(baseName))
        {
            throw new CliException(
                $"Discovered command '{discoveredCommand}' cannot be exposed as a .NET tool command.",
                "invalid_tool_command",
                ExitCodes.Usage);
        }

        return style == ToolCommandStyle.Dotnet
            ? new ToolCommandIdentity(baseName, style, $"dotnet-{baseName}", $"dotnet {baseName}")
            : new ToolCommandIdentity(baseName, style, baseName, baseName);
    }

    public static ToolCommandStyle InferInstalledStyle(InstallationRecord installation)
    {
        if (Enum.TryParse<ToolCommandStyle>(installation.CommandStyle, ignoreCase: true, out var stored))
        {
            return stored;
        }

        return installation.Command?.StartsWith("dotnet ", StringComparison.OrdinalIgnoreCase) == true ||
               installation.Command?.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase) == true
            ? ToolCommandStyle.Dotnet
            : ToolCommandStyle.Standalone;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
    private static partial Regex ValidCommandName();
}
