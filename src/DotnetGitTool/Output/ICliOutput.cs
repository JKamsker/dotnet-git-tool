using DotnetGitTool.Commands;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.State;

namespace DotnetGitTool.Output;

public interface ICliOutput
{
    void Status(GlobalSettings settings, string message);
    void Diagnostic(GlobalSettings settings, string message);
    void Success(GlobalSettings settings, object data, string humanMessage);
    void Failure(GlobalSettings settings, CliException exception);
    void List(GlobalSettings settings, IReadOnlyList<InstallationRecord> installations);
}
