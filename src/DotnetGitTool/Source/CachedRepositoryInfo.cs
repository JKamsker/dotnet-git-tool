using DotnetGitTool.State;

namespace DotnetGitTool.Source;

public sealed record CachedRepositoryInfo(
    string SourceId,
    string RepositoryName,
    string Path,
    string? Origin,
    string? Branch,
    string? Commit,
    string? Revision,
    DateTimeOffset? CommitDate,
    bool IsGitRepository,
    bool? IsDirty,
    long? SizeBytes,
    InstallationRecord? Installation)
{
    public bool IsManaged => Installation is not null;
}

public sealed record RepositoryCacheInventory(
    string RepositoryRoot,
    IReadOnlyList<CachedRepositoryInfo> Repositories);
