using Spectre.Console.Cli;
using System.ComponentModel;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Workflows;

namespace DotnetGitTool.Commands;

public class GlobalSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit the stable v1 JSON envelope.")]
    public bool Json { get; init; }

    [CommandOption("--quiet")]
    [Description("Suppress status output and never prompt.")]
    public bool Quiet { get; init; }

    [CommandOption("--verbose")]
    [Description("Show resolved source, project, commit, and package details.")]
    public bool Verbose { get; init; }

    [CommandOption("--no-color")]
    [Description("Disable ANSI styling (also honored through NO_COLOR).")]
    public bool NoColor { get; init; }
}

public class MutationSettings : GlobalSettings
{
    [CommandOption("--dry-run")]
    [Description("Preview the operation without cloning, building, or changing state.")]
    public bool DryRun { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Confirm the requested mutation without prompting.")]
    public bool Yes { get; init; }
}

public class ToolCommandSettings : MutationSettings
{
    [CommandOption("--standalone")]
    [Description("Expose an unprefixed command, such as 'bookmeta'.")]
    public bool Standalone { get; init; }

    [CommandOption("--dotnet-command")]
    [Description("Expose a .NET subcommand, such as 'dotnet bookmeta' (the install default).")]
    public bool DotnetCommand { get; init; }

    public ToolCommandStyle? ResolveCommandStyleOverride()
    {
        if (Standalone && DotnetCommand)
        {
            throw new CliException(
                "--standalone and --dotnet-command cannot be used together.",
                "invalid_command_style",
                ExitCodes.Usage);
        }

        return Standalone ? ToolCommandStyle.Standalone : DotnetCommand ? ToolCommandStyle.Dotnet : null;
    }
}
