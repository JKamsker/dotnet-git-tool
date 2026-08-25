using DotnetGitTool.Infrastructure;
using DotnetGitTool.Output;
using DotnetGitTool.Source;
using DotnetGitTool.Workflows;
using Spectre.Console.Cli;

namespace DotnetGitTool.Commands;

public sealed class CachePruneSettings : MutationSettings
{
}

public sealed class CachePruneCommand(
    RepositoryCachePruner pruner,
    ICliOutput output) : AsyncCommand<CachePruneSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        CachePruneSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var plan = await pruner.CreatePlanAsync(cancellationToken);
            if (settings.DryRun)
            {
                output.Success(
                    settings,
                    new
                    {
                        action = "cache_prune_preview",
                        repositoryRoot = plan.RepositoryRoot,
                        unusedRepositoryPaths = plan.UnusedRepositoryPaths,
                    },
                    PreviewMessage(plan));
                return ExitCodes.Success;
            }

            InteractionGuard.ConfirmCachePrune(
                settings,
                plan.UnusedRepositoryPaths.Count,
                plan.RepositoryRoot);
            var result = pruner.Prune(plan);
            output.Success(
                settings,
                new
                {
                    action = "cache_pruned",
                    repositoryRoot = plan.RepositoryRoot,
                    removedRepositoryPaths = result.RemovedRepositoryPaths,
                    skippedInUseRepositoryPaths = result.SkippedInUseRepositoryPaths,
                },
                ResultMessage(result));
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

    private static string PreviewMessage(RepositoryPrunePlan plan)
    {
        if (plan.UnusedRepositoryPaths.Count == 0)
        {
            return $"No unused repositories found in {plan.RepositoryRoot}.";
        }

        var paths = string.Join(Environment.NewLine, plan.UnusedRepositoryPaths.Select(path => $"  {path}"));
        return $"Would remove {CountRepositories(plan.UnusedRepositoryPaths.Count)}:{Environment.NewLine}{paths}";
    }

    private static string ResultMessage(RepositoryPruneResult result)
    {
        var message = $"Removed {CountRepositories(result.RemovedRepositoryPaths.Count)}.";
        return result.SkippedInUseRepositoryPaths.Count == 0
            ? message
            : $"{message} Skipped {CountRepositories(result.SkippedInUseRepositoryPaths.Count)} currently in use.";
    }

    private static string CountRepositories(int count)
        => $"{count} unused cached {(count == 1 ? "repository" : "repositories")}";
}
