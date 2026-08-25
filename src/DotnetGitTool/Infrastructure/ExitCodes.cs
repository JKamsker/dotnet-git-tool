namespace DotnetGitTool.Infrastructure;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int GeneralError = 1;
    public const int Usage = 2;
    public const int NotFound = 5;
    public const int Conflict = 6;
    public const int Cancelled = 10;
}

public class CliException(string message, string kind, int exitCode = ExitCodes.GeneralError)
    : Exception(message)
{
    public string Kind { get; } = kind;
    public int ExitCode { get; } = exitCode;
}
