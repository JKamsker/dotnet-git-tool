using DotnetGitTool.Commands;
using DotnetGitTool.Infrastructure;

namespace DotnetGitTool.Workflows;

internal static class InteractionGuard
{
    public static void ConfirmCodeExecution(MutationSettings settings, string source)
    {
        if (settings.DryRun || settings.Yes)
        {
            return;
        }

        const string alternative = "Inspect with --dry-run or explicitly consent with --yes.";
        if (settings.Json || settings.Quiet || Console.IsInputRedirected || Console.IsErrorRedirected)
        {
            throw new CliException(
                $"Building '{source}' can execute arbitrary repository code. {alternative}",
                "confirmation_required",
                ExitCodes.Usage);
        }

        Console.Error.WriteLine($"Warning: building '{source}' can execute arbitrary code from that repository.");
        Console.Error.Write("Continue? [y/N] ");
        var answer = Console.ReadLine();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("Operation cancelled.", "cancelled", ExitCodes.Cancelled);
        }
    }

    public static void ConfirmUninstall(MutationSettings settings, string source)
    {
        if (settings.DryRun || settings.Yes)
        {
            return;
        }

        if (settings.Json || settings.Quiet || Console.IsInputRedirected || Console.IsErrorRedirected)
        {
            throw new CliException(
                $"Uninstalling '{source}' requires confirmation. Inspect with --dry-run or confirm with --yes.",
                "confirmation_required",
                ExitCodes.Usage);
        }

        Console.Error.Write($"Uninstall '{source}'? [y/N] ");
        var answer = Console.ReadLine();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("Operation cancelled.", "cancelled", ExitCodes.Cancelled);
        }
    }

    public static void ConfirmCachePrune(MutationSettings settings, int repositoryCount, string repositoryRoot)
    {
        if (repositoryCount == 0 || settings.DryRun || settings.Yes)
        {
            return;
        }

        const string alternative = "Inspect with --dry-run or explicitly confirm with --yes.";
        if (settings.Json || settings.Quiet || Console.IsInputRedirected || Console.IsErrorRedirected)
        {
            throw new CliException(
                $"Removing {repositoryCount} unused cached repositories requires confirmation. {alternative}",
                "confirmation_required",
                ExitCodes.Usage);
        }

        Console.Error.Write($"Remove {repositoryCount} unused cached repositories from '{repositoryRoot}'? [y/N] ");
        var answer = Console.ReadLine();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("Operation cancelled.", "cancelled", ExitCodes.Cancelled);
        }
    }
}
