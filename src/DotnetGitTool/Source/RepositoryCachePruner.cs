using DotnetGitTool.Infrastructure;
using DotnetGitTool.State;

namespace DotnetGitTool.Source;

public sealed class RepositoryCachePruner(
    RepositoryCachePath cachePath,
    RepositoryCache repositoryCache,
    InstallationStore installationStore)
{
    private const int LockBufferSize = 1;

    public async Task<RepositoryPrunePlan> CreatePlanAsync(CancellationToken cancellationToken = default)
    {
        var repositoryRoot = Path.Combine(cachePath.Value, "repositories");
        if (!Directory.Exists(repositoryRoot))
        {
            return new RepositoryPrunePlan(repositoryRoot, []);
        }

        var installations = await installationStore.ListAsync(cancellationToken);
        var usedPaths = new HashSet<string>(PathComparer());
        foreach (var installation in installations)
        {
            if (!string.IsNullOrWhiteSpace(installation.RepositoryPath))
            {
                usedPaths.Add(Path.GetFullPath(installation.RepositoryPath));
            }

            var source = new SourceSpec(
                installation.CloneUrl,
                installation.SourceId,
                installation.RequestedRef);
            usedPaths.Add(Path.GetFullPath(repositoryCache.GetRepositoryPath(source)));
        }

        var unusedPaths = Directory.EnumerateDirectories(repositoryRoot)
            .Select(Path.GetFullPath)
            .Where(path => !usedPaths.Contains(path))
            .Order(PathComparer())
            .ToArray();
        return new RepositoryPrunePlan(repositoryRoot, unusedPaths);
    }

    public RepositoryPruneResult Prune(RepositoryPrunePlan plan)
    {
        var lockRoot = Path.Combine(cachePath.Value, "locks");
        Directory.CreateDirectory(lockRoot);
        var removed = new List<string>(plan.UnusedRepositoryPaths.Count);
        var skippedInUse = new List<string>();

        foreach (var repositoryPath in plan.UnusedRepositoryPaths)
        {
            EnsureDirectChild(plan.RepositoryRoot, repositoryPath);
            var lockPath = Path.Combine(lockRoot, $"{Path.GetFileName(repositoryPath)}.lock");
            FileStream repositoryLock;
            try
            {
                repositoryLock = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    LockBufferSize);
            }
            catch (IOException)
            {
                skippedInUse.Add(repositoryPath);
                continue;
            }

            using (repositoryLock)
            {
                if (!Directory.Exists(repositoryPath))
                {
                    continue;
                }

                try
                {
                    var directory = new DirectoryInfo(repositoryPath);
                    if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        directory.Delete();
                    }
                    else
                    {
                        MakeWritable(directory);
                        directory.Delete(recursive: true);
                    }

                    removed.Add(repositoryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new CliException(
                        $"Could not remove cached repository '{repositoryPath}': {exception.Message}",
                        "cache_prune_failed");
                }
            }
        }

        return new RepositoryPruneResult(removed, skippedInUse);
    }

    private void EnsureDirectChild(string repositoryRoot, string repositoryPath)
    {
        var expectedRoot = Path.GetFullPath(Path.Combine(cachePath.Value, "repositories"));
        var resolvedRoot = Path.GetFullPath(repositoryRoot);
        var resolvedPath = Path.GetFullPath(repositoryPath);
        var parent = Path.GetDirectoryName(resolvedPath);
        if (!PathComparer().Equals(expectedRoot, resolvedRoot) ||
            parent is null ||
            !PathComparer().Equals(resolvedRoot, parent))
        {
            throw new CliException(
                $"Refusing to prune path outside the repository cache: '{repositoryPath}'.",
                "invalid_cache_prune_path",
                ExitCodes.Usage);
        }
    }

    private static void MakeWritable(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            file.IsReadOnly = false;
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            if (!child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                MakeWritable(child);
            }
        }
    }

    private static StringComparer PathComparer()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}

public sealed record RepositoryPrunePlan(
    string RepositoryRoot,
    IReadOnlyList<string> UnusedRepositoryPaths);

public sealed record RepositoryPruneResult(
    IReadOnlyList<string> RemovedRepositoryPaths,
    IReadOnlyList<string> SkippedInUseRepositoryPaths);
