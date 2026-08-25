using DotnetGitTool.Processes;

namespace DotnetGitTool.Source;

public sealed class RepositoryCloner(IProcessRunner processes)
{
    public async Task<ClonedRepository> CloneAsync(SourceSpec source, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-git-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var repositoryPath = Path.Combine(root, "repo");

        try
        {
            (await processes.RunAsync("git", ["clone", "--depth", "1", "--no-tags", source.CloneUrl, repositoryPath],
                    cancellationToken: cancellationToken))
                .EnsureSuccess($"Cloning {source.SourceId}");

            if (source.RequestedRef is not null)
            {
                (await processes.RunAsync("git", ["fetch", "--depth", "1", "origin", source.RequestedRef], repositoryPath,
                        cancellationToken))
                    .EnsureSuccess($"Fetching ref {source.RequestedRef}");
                (await processes.RunAsync("git", ["checkout", "--detach", "FETCH_HEAD"], repositoryPath,
                        cancellationToken))
                    .EnsureSuccess($"Checking out ref {source.RequestedRef}");
            }

            var revision = (await processes.RunAsync("git", ["rev-parse", "HEAD"], repositoryPath, cancellationToken))
                .EnsureSuccess("Resolving the cloned commit")
                .StandardOutput.Trim();
            return new ClonedRepository(root, repositoryPath, revision);
        }
        catch
        {
            TryDelete(root);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class ClonedRepository(string temporaryRoot, string path, string commit) : IDisposable
{
    public string Path { get; } = path;
    public string Commit { get; } = commit;

    public void Dispose()
    {
        try
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
