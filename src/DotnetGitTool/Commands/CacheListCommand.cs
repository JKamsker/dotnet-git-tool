using DotnetGitTool.Infrastructure;
using DotnetGitTool.Output;
using DotnetGitTool.Source;
using Spectre.Console.Cli;

namespace DotnetGitTool.Commands;

public sealed class CacheListSettings : GlobalSettings
{
}

public sealed class CacheListCommand(
    RepositoryCacheInspector inspector,
    CacheRepositoryTableRenderer tableRenderer,
    ICliOutput output) : AsyncCommand<CacheListSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        CacheListSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var inventory = await inspector.ListAsync(cancellationToken);
            if (settings.Json)
            {
                output.QueryResult(
                    settings,
                    new
                    {
                        repositoryRoot = inventory.RepositoryRoot,
                        repositories = inventory.Repositories,
                    },
                    string.Empty);
            }
            else
            {
                tableRenderer.Render(inventory);
            }

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
