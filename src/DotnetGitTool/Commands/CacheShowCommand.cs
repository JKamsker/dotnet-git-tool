using System.ComponentModel;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Output;
using DotnetGitTool.Source;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetGitTool.Commands;

public sealed class CacheShowSettings : GlobalSettings
{
    [CommandArgument(0, "<REPOSITORY>")]
    [Description("Source ID, repository name, package ID, or cache directory name.")]
    public string Repository { get; init; } = string.Empty;

    public override ValidationResult Validate()
        => string.IsNullOrWhiteSpace(Repository)
            ? ValidationResult.Error("A repository name is required.")
            : ValidationResult.Success();
}

public sealed class CacheShowCommand(
    RepositoryCacheInspector inspector,
    ICliOutput output) : AsyncCommand<CacheShowSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        CacheShowSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var repository = await inspector.ShowAsync(settings.Repository, cancellationToken);
            output.QueryResult(settings, new { repository }, CacheRepositoryFormatter.Show(repository));
            return ExitCodes.Success;
        }
        catch (CliException exception)
        {
            output.Failure(settings, exception);
            return exception.ExitCode;
        }
        catch (OperationCanceledException)
        {
            var exception = new CliException("Operation cancelled.", "cancelled", ExitCodes.Cancelled);
            output.Failure(settings, exception);
            return exception.ExitCode;
        }
        catch (Exception exception)
        {
            var failure = new CliException(exception.Message, "unexpected_error");
            output.Failure(settings, failure);
            return failure.ExitCode;
        }
    }
}
