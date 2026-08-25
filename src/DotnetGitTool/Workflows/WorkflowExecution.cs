using DotnetGitTool.Commands;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Output;

namespace DotnetGitTool.Workflows;

public sealed class WorkflowExecution(ICliOutput output)
{
    public async Task<int> RunAsync(GlobalSettings settings, Func<Task<int>> action)
    {
        try
        {
            return await action();
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
