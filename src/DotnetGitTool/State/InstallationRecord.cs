namespace DotnetGitTool.State;

public sealed record InstallationRecord(
    string SourceId,
    string CloneUrl,
    string? RequestedRef,
    string Project,
    string PackageId,
    string Version,
    string Commit,
    string? Command,
    string? CommandStyle,
    string? RepositoryPath,
    DateTimeOffset InstalledAt);

internal sealed record InstallationState(int SchemaVersion, List<InstallationRecord> Installations)
{
    public static InstallationState Empty => new(1, []);
}
