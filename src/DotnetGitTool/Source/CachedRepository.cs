using DotnetGitTool.Infrastructure;
using DotnetGitTool.Processes;

namespace DotnetGitTool.Source;

public sealed class CachedRepository(
    string path,
    FileStream repositoryLock,
    IProcessRunner processes) : IAsyncDisposable
{
    public string Path { get; } = path;
    public string Commit { get; private set; } = string.Empty;

    internal void SetCommit(string commit) => Commit = commit;

    public async Task CleanAsync(CancellationToken cancellationToken = default)
    {
        (await processes.RunAsync("git", ["reset", "--hard", "HEAD"], Path, cancellationToken))
            .EnsureSuccess("Resetting the cached repository");
        (await processes.RunAsync(
                "git",
                ["submodule", "foreach", "--recursive", "git reset --hard HEAD && git clean -ffdx"],
                Path,
                cancellationToken))
            .EnsureSuccess("Resetting cached submodules");
        (await processes.RunAsync("git", ["clean", "-ffdx"], Path, cancellationToken))
            .EnsureSuccess("Cleaning build artifacts from the cached repository");
        var status = (await processes.RunAsync(
                "git",
                ["status", "--porcelain", "--untracked-files=all"],
                Path,
                cancellationToken))
            .EnsureSuccess("Verifying the cached repository");
        if (!string.IsNullOrWhiteSpace(status.StandardOutput))
        {
            throw new CliException(
                $"Repository cache '{Path}' is still dirty after cleanup.",
                "repository_cache_dirty");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CleanAsync(CancellationToken.None);
        }
        finally
        {
            await repositoryLock.DisposeAsync();
        }
    }
}
