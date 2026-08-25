using DotnetGitTool.Infrastructure;
using DotnetGitTool.Output;
using DotnetGitTool.State;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetGitTool.Commands;

public sealed class ListSettings : GlobalSettings
{
}

public sealed class ListCommand(InstallationStore store, ICliOutput output) : AsyncCommand<ListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ListSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var installations = await store.ListAsync(cancellationToken);
            output.List(settings, installations);
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
