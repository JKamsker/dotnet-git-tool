using DotnetGitTool.Infrastructure;
using DotnetGitTool.Processes;
using DotnetGitTool.Source;
using DotnetGitTool.State;

namespace DotnetGitTool.Tests;

public sealed class RepositoryCacheInspectorTests
{
    [Fact]
    public async Task ListsGitAndManagedInstallationMetadataWithFullPath()
    {
        using var fixture = new InspectorFixture();
        var expectedCommit = await fixture.CreateRepositoryAsync();
        await fixture.InstallationStore.AddAsync(
            fixture.Installation(expectedCommit),
            TestContext.Current.CancellationToken);

        var inventory = await fixture.Inspector.ListAsync(TestContext.Current.CancellationToken);

        var repository = Assert.Single(inventory.Repositories);
        Assert.Equal(fixture.RepositoryPath, repository.Path);
        Assert.Equal(fixture.Source.SourceId, repository.SourceId);
        Assert.Equal(fixture.Source.CloneUrl, repository.Origin);
        Assert.Equal(expectedCommit, repository.Commit);
        Assert.Equal("main", repository.Branch);
        Assert.NotNull(repository.CommitDate);
        Assert.True(repository.IsGitRepository);
        Assert.False(repository.IsDirty);
        Assert.True(repository.IsManaged);
        Assert.Equal("1.2.3", repository.SourceVersion);
        Assert.Equal("1.2.3", repository.Installation!.Version);
        Assert.Equal(InspectorFixture.InstalledAt, repository.Installation.InstalledAt);
        Assert.Equal(InspectorFixture.UpdatedAt, repository.Installation.UpdatedAt);
    }

    [Fact]
    public async Task ShowResolvesRepositoryNameAndIncludesSizeAndDirtyState()
    {
        using var fixture = new InspectorFixture();
        await fixture.CreateRepositoryAsync();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.RepositoryPath, "untracked.txt"),
            "dirty",
            TestContext.Current.CancellationToken);

        var repository = await fixture.Inspector.ShowAsync(
            "repository",
            TestContext.Current.CancellationToken);

        Assert.True(repository.IsDirty);
        Assert.True(repository.SizeBytes > 0);
        Assert.Equal(fixture.RepositoryPath, repository.Path);
    }

    [Fact]
    public void RejectsAmbiguousShortRepositoryName()
    {
        var repositories = new[]
        {
            Repository("one/shared", "one"),
            Repository("two/shared", "two"),
        };

        var exception = Assert.Throws<CliException>(
            () => RepositoryCacheInspector.Resolve(repositories, "shared"));

        Assert.Equal("ambiguous_cache_repository", exception.Kind);
        Assert.Contains("one/shared", exception.Message);
        Assert.Contains("two/shared", exception.Message);
    }

    private static CachedRepositoryInfo Repository(string sourceId, string directory)
        => new(
            sourceId,
            "shared",
            Path.Combine(Path.GetTempPath(), directory),
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null);

    private sealed class InspectorFixture : IDisposable
    {
        public static readonly DateTimeOffset InstalledAt = DateTimeOffset.Parse("2026-01-02T03:04:05Z");
        public static readonly DateTimeOffset UpdatedAt = DateTimeOffset.Parse("2026-02-03T04:05:06Z");

        private readonly DirectoryInfo temporaryRoot =
            Directory.CreateTempSubdirectory("dotnet-git-tool-inspector-tests-");
        private readonly ProcessRunner processes = new();

        public InspectorFixture()
        {
            var cachePath = new RepositoryCachePath(Path.Combine(temporaryRoot.FullName, "cache"));
            var repositoryCache = new RepositoryCache(processes, cachePath);
            Source = new SourceSpec("https://github.com/owner/repository.git", "owner/repository", null);
            RepositoryPath = repositoryCache.GetRepositoryPath(Source);
            InstallationStore = new InstallationStore(
                new InstallationStorePath(Path.Combine(temporaryRoot.FullName, "state")));
            Inspector = new RepositoryCacheInspector(
                cachePath,
                repositoryCache,
                InstallationStore,
                new SourceSpecParser(),
                new ProjectVersionReader(),
                processes);
        }

        public SourceSpec Source { get; }
        public string RepositoryPath { get; }
        public InstallationStore InstallationStore { get; }
        public RepositoryCacheInspector Inspector { get; }

        public async Task<string> CreateRepositoryAsync()
        {
            Directory.CreateDirectory(RepositoryPath);
            var cancellationToken = TestContext.Current.CancellationToken;
            await GitAsync("init", "--initial-branch=main");
            await GitAsync("config", "user.name", "Inspector Test");
            await GitAsync("config", "user.email", "inspector@example.invalid");
            Directory.CreateDirectory(Path.Combine(RepositoryPath, "src"));
            await File.WriteAllTextAsync(
                Path.Combine(RepositoryPath, "src", "tool.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>",
                cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "source.txt"), "content", cancellationToken);
            await GitAsync("add", ".");
            await GitAsync("commit", "-m", "initial");
            await GitAsync("remote", "add", "origin", Source.CloneUrl);
            return (await GitAsync("rev-parse", "HEAD")).StandardOutput.Trim();
        }

        public InstallationRecord Installation(string commit)
            => new(
                Source.SourceId,
                Source.CloneUrl,
                null,
                "src/tool.csproj",
                "git.owner.repository",
                "1.2.3",
                commit,
                "dotnet repository",
                "dotnet",
                RepositoryPath,
                InstalledAt,
                UpdatedAt);

        public void Dispose()
        {
            foreach (var file in temporaryRoot.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.IsReadOnly = false;
            }

            temporaryRoot.Delete(recursive: true);
        }

        private async Task<ProcessResult> GitAsync(params string[] arguments)
        {
            var result = await processes.RunAsync(
                "git",
                arguments,
                RepositoryPath,
                TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, result.StandardError);
            return result;
        }
    }
}
