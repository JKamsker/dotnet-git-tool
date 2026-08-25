using DotnetGitTool.Infrastructure;
using DotnetGitTool.Processes;
using DotnetGitTool.Source;
using DotnetGitTool.State;

namespace DotnetGitTool.Tests;

public sealed class RepositoryCachePrunerTests
{
    [Fact]
    public async Task RemovesUnusedRepositoryAndPreservesManagedRepository()
    {
        using var fixture = new PrunerFixture();
        var source = new SourceSpec("https://github.com/owner/managed.git", "owner/managed", null);
        var managedPath = fixture.RepositoryCache.GetRepositoryPath(source);
        var unusedPath = Path.Combine(fixture.RepositoryRoot, "owner-unused-0123456789ab");
        Directory.CreateDirectory(managedPath);
        Directory.CreateDirectory(unusedPath);
        var readOnlyFile = new FileInfo(Path.Combine(unusedPath, "read-only.txt"));
        await File.WriteAllTextAsync(readOnlyFile.FullName, "cached", TestContext.Current.CancellationToken);
        readOnlyFile.IsReadOnly = true;
        await fixture.InstallationStore.AddAsync(
            Installation(source, repositoryPath: null),
            TestContext.Current.CancellationToken);

        var plan = await fixture.Pruner.CreatePlanAsync(TestContext.Current.CancellationToken);
        var result = fixture.Pruner.Prune(plan);

        Assert.Equal(unusedPath, Assert.Single(result.RemovedRepositoryPaths));
        Assert.Empty(result.SkippedInUseRepositoryPaths);
        Assert.True(Directory.Exists(managedPath));
        Assert.False(Directory.Exists(unusedPath));
    }

    [Fact]
    public async Task SkipsRepositoryLockedByAnotherOperation()
    {
        using var fixture = new PrunerFixture();
        var unusedPath = Path.Combine(fixture.RepositoryRoot, "owner-locked-0123456789ab");
        Directory.CreateDirectory(unusedPath);
        Directory.CreateDirectory(fixture.LockRoot);
        var lockPath = Path.Combine(fixture.LockRoot, $"{Path.GetFileName(unusedPath)}.lock");
        await using var repositoryLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        var plan = await fixture.Pruner.CreatePlanAsync(TestContext.Current.CancellationToken);

        var result = fixture.Pruner.Prune(plan);

        Assert.Empty(result.RemovedRepositoryPaths);
        Assert.Equal(unusedPath, Assert.Single(result.SkippedInUseRepositoryPaths));
        Assert.True(Directory.Exists(unusedPath));
    }

    [Fact]
    public async Task EmptyCacheProducesEmptyPlan()
    {
        using var fixture = new PrunerFixture(createCache: false);

        var plan = await fixture.Pruner.CreatePlanAsync(TestContext.Current.CancellationToken);

        Assert.Empty(plan.UnusedRepositoryPaths);
        Assert.Equal(fixture.RepositoryRoot, plan.RepositoryRoot);
    }

    [Fact]
    public void RefusesPathOutsideRepositoryRoot()
    {
        using var fixture = new PrunerFixture();
        var outsidePath = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!, "outside");
        Directory.CreateDirectory(outsidePath);
        var plan = new RepositoryPrunePlan(fixture.RepositoryRoot, [outsidePath]);

        var exception = Assert.Throws<CliException>(() => fixture.Pruner.Prune(plan));

        Assert.Equal("invalid_cache_prune_path", exception.Kind);
        Assert.True(Directory.Exists(outsidePath));
    }

    private static InstallationRecord Installation(SourceSpec source, string? repositoryPath)
        => new(
            source.SourceId,
            source.CloneUrl,
            source.RequestedRef,
            "src/tool.csproj",
            "git.owner.managed",
            "1.0.0",
            "0123456789abcdef",
            "dotnet managed",
            "dotnet",
            repositoryPath,
            DateTimeOffset.UnixEpoch);

    private sealed class PrunerFixture : IDisposable
    {
        private readonly DirectoryInfo temporaryRoot;

        public PrunerFixture(bool createCache = true)
        {
            temporaryRoot = Directory.CreateTempSubdirectory("dotnet-git-tool-prune-tests-");
            var cachePath = new RepositoryCachePath(Path.Combine(temporaryRoot.FullName, "cache"));
            RepositoryRoot = Path.Combine(cachePath.Value, "repositories");
            LockRoot = Path.Combine(cachePath.Value, "locks");
            if (createCache)
            {
                Directory.CreateDirectory(RepositoryRoot);
            }

            RepositoryCache = new RepositoryCache(new ProcessRunner(), cachePath);
            InstallationStore = new InstallationStore(
                new InstallationStorePath(Path.Combine(temporaryRoot.FullName, "state")));
            Pruner = new RepositoryCachePruner(cachePath, RepositoryCache, InstallationStore);
        }

        public string RepositoryRoot { get; }
        public string LockRoot { get; }
        public RepositoryCache RepositoryCache { get; }
        public InstallationStore InstallationStore { get; }
        public RepositoryCachePruner Pruner { get; }

        public void Dispose()
        {
            foreach (var file in temporaryRoot.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.IsReadOnly = false;
            }

            temporaryRoot.Delete(recursive: true);
        }
    }
}
