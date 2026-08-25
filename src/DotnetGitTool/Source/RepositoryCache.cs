using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Processes;

namespace DotnetGitTool.Source;

public sealed partial class RepositoryCache(IProcessRunner processes, RepositoryCachePath cachePath)
{
    public string GetRepositoryPath(SourceSpec source)
    {
        var safeName = UnsafePathCharacter().Replace(source.SourceId, "-").Trim('-');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.SourceId)))[..12].ToLowerInvariant();
        return Path.Combine(cachePath.Value, "repositories", $"{safeName}-{hash}");
    }

    public async Task<CachedRepository> PrepareAsync(
        SourceSpec source,
        CancellationToken cancellationToken = default)
    {
        var repositoryPath = GetRepositoryPath(source);
        var cacheDirectory = cachePath.Value;
        Directory.CreateDirectory(Path.Combine(cacheDirectory, "repositories"));
        Directory.CreateDirectory(Path.Combine(cacheDirectory, "locks"));
        var lockPath = Path.Combine(cacheDirectory, "locks", $"{Path.GetFileName(repositoryPath)}.lock");
        var repositoryLock = await AcquireLockAsync(lockPath, cancellationToken);
        var cacheExisted = Directory.Exists(repositoryPath);
        CachedRepository? repository = null;

        try
        {
            if (Directory.Exists(repositoryPath))
            {
                await ValidateCacheAsync(repositoryPath, source, cancellationToken);
                repository = new CachedRepository(repositoryPath, repositoryLock, processes);
                await repository.CleanAsync(cancellationToken);
                await SynchronizeAsync(repositoryPath, source, cancellationToken);
                return await CompleteAsync(repository, cancellationToken);
            }

            (await processes.RunAsync(
                    "git",
                    ["clone", "--depth", "1", "--no-tags", source.CloneUrl, repositoryPath],
                    cancellationToken: cancellationToken))
                .EnsureSuccess($"Cloning {source.SourceId}");
            repository = new CachedRepository(repositoryPath, repositoryLock, processes);
            if (source.RequestedRef is not null)
            {
                await CheckoutRefAsync(repositoryPath, source.RequestedRef, cancellationToken);
            }

            return await CompleteAsync(repository, cancellationToken);
        }
        catch
        {
            if (repository is not null)
            {
                await repository.DisposeAsync();
            }
            else
            {
                if (!cacheExisted)
                {
                    TryDeleteIncompleteClone(repositoryPath);
                }

                await repositoryLock.DisposeAsync();
            }

            throw;
        }
    }

    private async Task SynchronizeAsync(string path, SourceSpec source, CancellationToken cancellationToken)
    {
        (await processes.RunAsync("git", ["remote", "set-url", "origin", source.CloneUrl], path, cancellationToken))
            .EnsureSuccess("Refreshing the cached repository origin");
        if (source.RequestedRef is not null)
        {
            await CheckoutRefAsync(path, source.RequestedRef, cancellationToken);
            return;
        }

        var defaultBranch = await ResolveDefaultBranchAsync(path, cancellationToken);
        (await processes.RunAsync(
                "git",
                ["fetch", "--depth", "1", "origin", $"+refs/heads/{defaultBranch}:refs/remotes/origin/{defaultBranch}"],
                path,
                cancellationToken))
            .EnsureSuccess($"Fetching origin/{defaultBranch}");
        (await processes.RunAsync(
                "git",
                ["checkout", "-B", defaultBranch, $"refs/remotes/origin/{defaultBranch}"],
                path,
                cancellationToken))
            .EnsureSuccess($"Checking out origin/{defaultBranch}");
    }

    private async Task CheckoutRefAsync(string path, string requestedRef, CancellationToken cancellationToken)
    {
        var fetch = await processes.RunAsync(
            "git",
            ["fetch", "--depth", "1", "origin", requestedRef],
            path,
            cancellationToken);
        if (!fetch.Succeeded && AbbreviatedCommit().IsMatch(requestedRef))
        {
            var resolvedCommit = await ResolveCommitAsync(path, requestedRef, cancellationToken);
            if (resolvedCommit is null)
            {
                await FetchReachableHistoryAsync(path, cancellationToken);
                resolvedCommit = await ResolveCommitAsync(path, requestedRef, cancellationToken);
            }

            if (resolvedCommit is null)
            {
                throw new CliException(
                    $"Could not resolve abbreviated commit '{requestedRef}' in reachable branch history. " +
                    "Use the full 40-character commit hash if the commit is no longer reachable from a branch.",
                    "ref_not_found",
                    ExitCodes.NotFound);
            }

            (await processes.RunAsync("git", ["checkout", "--detach", resolvedCommit], path, cancellationToken))
                .EnsureSuccess($"Checking out commit {resolvedCommit}");
            return;
        }

        fetch.EnsureSuccess($"Fetching ref {requestedRef}");
        (await processes.RunAsync("git", ["checkout", "--detach", "FETCH_HEAD"], path, cancellationToken))
            .EnsureSuccess($"Checking out ref {requestedRef}");
    }

    private async Task FetchReachableHistoryAsync(string path, CancellationToken cancellationToken)
    {
        var shallow = (await processes.RunAsync(
                "git",
                ["rev-parse", "--is-shallow-repository"],
                path,
                cancellationToken))
            .EnsureSuccess("Inspecting the cached repository")
            .StandardOutput.Trim()
            .Equals("true", StringComparison.OrdinalIgnoreCase);
        var arguments = shallow
            ? new[]
            {
                "fetch", "--unshallow", "--no-tags", "origin",
                "+refs/heads/*:refs/remotes/origin/*",
            }
            : new[]
            {
                "fetch", "--no-tags", "origin",
                "+refs/heads/*:refs/remotes/origin/*",
            };
        (await processes.RunAsync("git", arguments, path, cancellationToken))
            .EnsureSuccess("Fetching branch history to resolve an abbreviated commit");
    }

    private async Task<string?> ResolveCommitAsync(
        string path,
        string abbreviatedCommit,
        CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(
            "git",
            ["rev-parse", "--verify", "--quiet", $"{abbreviatedCommit}^{{commit}}"],
            path,
            cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    private async Task<string> ResolveDefaultBranchAsync(string path, CancellationToken cancellationToken)
    {
        var result = (await processes.RunAsync("git", ["ls-remote", "--symref", "origin", "HEAD"], path, cancellationToken))
            .EnsureSuccess("Resolving the remote default branch");
        const string prefix = "ref: refs/heads/";
        var line = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        if (line is null)
        {
            throw new CliException("The remote repository did not report a default branch.", "default_branch_not_found");
        }

        var tab = line.IndexOf('\t');
        return line[prefix.Length..(tab < 0 ? line.Length : tab)];
    }

    private async Task ValidateCacheAsync(string path, SourceSpec source, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(path, ".git")))
        {
            throw new CliException(
                $"Cache path '{path}' exists but is not a Git repository. Move it aside and retry.",
                "invalid_repository_cache",
                ExitCodes.Conflict);
        }

        var remote = (await processes.RunAsync("git", ["remote", "get-url", "origin"], path, cancellationToken))
            .EnsureSuccess("Validating the cached repository origin")
            .StandardOutput.Trim();
        if (!remote.Equals(source.CloneUrl, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                $"Cache path '{path}' belongs to a different remote ('{remote}'). Move it aside and retry.",
                "repository_cache_conflict",
                ExitCodes.Conflict);
        }
    }

    private async Task<CachedRepository> CompleteAsync(
        CachedRepository repository,
        CancellationToken cancellationToken)
    {
        var revision = (await processes.RunAsync("git", ["rev-parse", "HEAD"], repository.Path, cancellationToken))
            .EnsureSuccess("Resolving the cached commit")
            .StandardOutput.Trim();
        repository.SetCommit(revision);
        return repository;
    }

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (IOException)
            {
                throw new CliException(
                    "Timed out waiting for another operation using this repository cache.",
                    "repository_cache_locked",
                    ExitCodes.Conflict);
            }
        }
    }

    private static void TryDeleteIncompleteClone(string path)
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

    [GeneratedRegex("[^A-Za-z0-9_.-]+")]
    private static partial Regex UnsafePathCharacter();

    [GeneratedRegex("^[0-9a-fA-F]{4,39}$")]
    private static partial Regex AbbreviatedCommit();
}

public sealed class RepositoryCachePath
{
    public RepositoryCachePath(string? path = null) => Value = path is null ? Resolve() : Path.GetFullPath(path);

    public string Value { get; }

    private static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_GIT_TOOL_CACHE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgCache))
        {
            return Path.Combine(xdgCache, "dotnet-git-tool");
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dotnet-git-tool",
                "cache");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "dotnet-git-tool");
    }
}

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
