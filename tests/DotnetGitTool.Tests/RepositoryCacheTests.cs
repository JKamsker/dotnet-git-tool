using DotnetGitTool.Processes;
using DotnetGitTool.Source;
using DotnetGitTool.Infrastructure;

namespace DotnetGitTool.Tests;

public sealed class RepositoryCacheTests
{
    [Fact]
    public async Task RefusesExistingNonRepositoryCacheWithoutDeletingIt()
    {
        var temporaryRoot = Directory.CreateTempSubdirectory("dotnet-git-tool-cache-conflict-tests-");
        try
        {
            var cache = new RepositoryCache(
                new ProcessRunner(),
                new RepositoryCachePath(Path.Combine(temporaryRoot.FullName, "cache")));
            var source = new SourceSpec("https://github.com/owner/repository.git", "owner/repository", null);
            var repositoryPath = cache.GetRepositoryPath(source);
            Directory.CreateDirectory(repositoryPath);
            var marker = Path.Combine(repositoryPath, "do-not-delete.txt");
            await File.WriteAllTextAsync(marker, "user data", TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<CliException>(
                () => cache.PrepareAsync(source, TestContext.Current.CancellationToken));

            Assert.Equal("invalid_repository_cache", exception.Kind);
            Assert.True(File.Exists(marker));
        }
        finally
        {
            temporaryRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReusesRepositoryPullsNewCommitAndRemovesAllArtifacts()
    {
        var temporaryRoot = Directory.CreateTempSubdirectory("dotnet-git-tool-cache-tests-");
        try
        {
            var origin = Path.Combine(temporaryRoot.FullName, "origin");
            Directory.CreateDirectory(origin);
            var processes = new ProcessRunner();
            var cancellationToken = TestContext.Current.CancellationToken;
            await GitAsync(processes, origin, cancellationToken, "init", "--initial-branch=main");
            await GitAsync(processes, origin, cancellationToken, "config", "user.name", "Cache Test");
            await GitAsync(processes, origin, cancellationToken, "config", "user.email", "cache@example.invalid");
            await File.WriteAllTextAsync(Path.Combine(origin, ".gitignore"), "bin/\nobj/\n", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(origin, "source.txt"), "first\n", cancellationToken);
            await GitAsync(processes, origin, cancellationToken, "add", ".");
            await GitAsync(processes, origin, cancellationToken, "commit", "-m", "first");

            var cache = new RepositoryCache(
                processes,
                new RepositoryCachePath(Path.Combine(temporaryRoot.FullName, "cache")));
            var source = new SourceSpec(origin, "owner/repository", null);
            string repositoryPath;
            string firstCommit;
            await using (var repository = await cache.PrepareAsync(source, cancellationToken))
            {
                repositoryPath = repository.Path;
                firstCommit = repository.Commit;
                await File.WriteAllTextAsync(Path.Combine(repository.Path, "source.txt"), "modified\n", cancellationToken);
                Directory.CreateDirectory(Path.Combine(repository.Path, "bin"));
                await File.WriteAllTextAsync(Path.Combine(repository.Path, "bin", "artifact.dll"), "artifact", cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(repository.Path, "untracked.tmp"), "artifact", cancellationToken);
            }

            Assert.True(Directory.Exists(repositoryPath));
            Assert.Equal("first\n", await File.ReadAllTextAsync(Path.Combine(repositoryPath, "source.txt"), cancellationToken));
            Assert.False(Directory.Exists(Path.Combine(repositoryPath, "bin")));
            Assert.False(File.Exists(Path.Combine(repositoryPath, "untracked.tmp")));
            Assert.Empty((await GitAsync(processes, repositoryPath, cancellationToken, "status", "--porcelain")).StandardOutput);

            await File.WriteAllTextAsync(Path.Combine(origin, "source.txt"), "second\n", cancellationToken);
            await GitAsync(processes, origin, cancellationToken, "add", "source.txt");
            await GitAsync(processes, origin, cancellationToken, "commit", "-m", "second");
            await using (var repository = await cache.PrepareAsync(source, cancellationToken))
            {
                Assert.Equal(repositoryPath, repository.Path);
                Assert.NotEqual(firstCommit, repository.Commit);
                Assert.Equal("second\n", await File.ReadAllTextAsync(
                    Path.Combine(repository.Path, "source.txt"), cancellationToken));
            }
        }
        finally
        {
            temporaryRoot.Delete(recursive: true);
        }
    }

    private static async Task<ProcessResult> GitAsync(
        IProcessRunner processes,
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await processes.RunAsync("git", arguments, workingDirectory, cancellationToken);
        Assert.True(result.Succeeded, result.StandardError);
        return result;
    }
}
