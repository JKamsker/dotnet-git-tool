using DotnetGitTool.Infrastructure;

namespace DotnetGitTool.Processes;

internal static class ProcessResultExtensions
{
    public static ProcessResult EnsureSuccess(this ProcessResult result, string operation)
    {
        if (result.Succeeded)
        {
            return result;
        }

        var detail = FirstUsefulLine(result.StandardError) ?? FirstUsefulLine(result.StandardOutput);
        var message = detail is null ? $"{operation} failed with exit code {result.ExitCode}." : $"{operation} failed: {detail}";
        throw new CliException(message, "child_process_failed");
    }

    private static string? FirstUsefulLine(string value)
        => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
}
