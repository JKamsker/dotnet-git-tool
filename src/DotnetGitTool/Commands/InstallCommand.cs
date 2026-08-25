using System.ComponentModel;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Workflows;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetGitTool.Commands;

public sealed class InstallSettings : ToolCommandSettings
{
    [CommandArgument(0, "<REPOSITORY>")]
    [Description("GitHub owner/repo with an optional @ref, or a Git repository URL.")]
    public string Repository { get; init; } = string.Empty;

    [CommandOption("--ref <REF>")]
    [Description("Branch, tag, or commit to install; overrides an embedded @ref.")]
    public string? Ref { get; init; }

    [CommandOption("-p|--project <PATH>")]
    [Description("Project file or directory inside the repository.")]
    public string? Project { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Repository))
        {
            return ValidationResult.Error("A repository is required.");
        }

        return ValidationResult.Success();
    }
}

public sealed class InstallCommand(ToolWorkflow workflow) : AsyncCommand<InstallSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, InstallSettings settings, CancellationToken cancellationToken)
        => workflow.InstallAsync(
            settings,
            settings.Repository,
            settings.Ref,
            settings.Project,
            cancellationToken);
}
