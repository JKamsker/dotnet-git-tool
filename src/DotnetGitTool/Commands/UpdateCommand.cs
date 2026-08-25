using System.ComponentModel;
using DotnetGitTool.Workflows;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetGitTool.Commands;

public sealed class UpdateSettings : ToolCommandSettings
{
    [CommandArgument(0, "<REPOSITORY>")]
    [Description("Previously installed owner/repo, optionally with a new @ref.")]
    public string Repository { get; init; } = string.Empty;

    [CommandOption("--ref <REF>")]
    [Description("Branch, tag, or commit to update to; overrides the recorded ref.")]
    public string? Ref { get; init; }

    [CommandOption("-p|--project <PATH>")]
    [Description("Override the recorded project file or directory.")]
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

public sealed class UpdateCommand(ToolWorkflow workflow) : AsyncCommand<UpdateSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, UpdateSettings settings, CancellationToken cancellationToken)
        => workflow.UpdateAsync(
            settings,
            settings.Repository,
            settings.Ref,
            settings.Project,
            cancellationToken);
}
