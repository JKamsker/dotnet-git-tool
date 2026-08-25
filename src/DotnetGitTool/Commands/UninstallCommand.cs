using System.ComponentModel;
using DotnetGitTool.Workflows;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetGitTool.Commands;

public sealed class UninstallSettings : MutationSettings
{
    [CommandArgument(0, "<REPOSITORY>")]
    [Description("Previously installed owner/repo or repository URL.")]
    public string Repository { get; init; } = string.Empty;

    public override ValidationResult Validate()
        => string.IsNullOrWhiteSpace(Repository)
            ? ValidationResult.Error("A repository is required.")
            : ValidationResult.Success();
}

public sealed class UninstallCommand(ToolWorkflow workflow) : AsyncCommand<UninstallSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, UninstallSettings settings, CancellationToken cancellationToken)
        => workflow.UninstallAsync(settings, settings.Repository, cancellationToken);
}
