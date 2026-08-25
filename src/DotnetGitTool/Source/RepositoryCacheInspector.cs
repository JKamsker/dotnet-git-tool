using System.Globalization;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Processes;
using DotnetGitTool.State;

namespace DotnetGitTool.Source;

public sealed class RepositoryCacheInspector(
    RepositoryCachePath cachePath,
    RepositoryCache repositoryCache,
    InstallationStore installationStore,
    SourceSpecParser sourceParser,
    IProcessRunner processes)
{
    public async Task<RepositoryCacheInventory> ListAsync(CancellationToken cancellationToken = default)
    {
        var repositoryRoot = Path.Combine(cachePath.Value, "repositories");
        if (!Directory.Exists(repositoryRoot))
        {
            return new RepositoryCacheInventory(repositoryRoot, []);
        }

        var installations = await installationStore.ListAsync(cancellationToken);
        var installationsByPath = IndexInstallations(installations);
        var inspectionTasks = Directory.EnumerateDirectories(repositoryRoot)
            .Order(PathComparer())
            .Select(path => InspectAsync(path, installationsByPath, cancellationToken));
        var repositories = await Task.WhenAll(inspectionTasks);
        return new RepositoryCacheInventory(repositoryRoot, repositories);
    }

    public async Task<CachedRepositoryInfo> ShowAsync(
        string selector,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            throw new CliException("A repository name is required.", "invalid_cache_repository", ExitCodes.Usage);
        }

        var inventory = await ListAsync(cancellationToken);
        var repository = Resolve(inventory.Repositories, selector.Trim());
        return repository with { SizeBytes = CalculateSize(repository.Path) };
    }

    public static CachedRepositoryInfo Resolve(
        IReadOnlyList<CachedRepositoryInfo> repositories,
        string selector)
    {
        var exactSourceMatches = repositories
            .Where(repository => repository.SourceId.Equals(selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactSourceMatches.Length == 1)
        {
            return exactSourceMatches[0];
        }

        var matches = repositories.Where(repository =>
                repository.RepositoryName.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(repository.Path).Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                repository.Installation?.PackageId.Equals(selector, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new CliException(
                $"Cached repository '{selector}' was not found.",
                "cache_repository_not_found",
                ExitCodes.NotFound),
            _ => throw new CliException(
                $"Cached repository name '{selector}' is ambiguous. Use one of: " +
                string.Join(", ", matches.Select(match => match.SourceId)),
                "ambiguous_cache_repository",
                ExitCodes.Usage),
        };
    }

    private Dictionary<string, InstallationRecord> IndexInstallations(
        IReadOnlyList<InstallationRecord> installations)
    {
        var result = new Dictionary<string, InstallationRecord>(PathComparer());
        foreach (var installation in installations)
        {
            if (!string.IsNullOrWhiteSpace(installation.RepositoryPath))
            {
                result[Path.GetFullPath(installation.RepositoryPath)] = installation;
            }

            var source = new SourceSpec(
                installation.CloneUrl,
                installation.SourceId,
                installation.RequestedRef);
            result[Path.GetFullPath(repositoryCache.GetRepositoryPath(source))] = installation;
        }

        return result;
    }

    private async Task<CachedRepositoryInfo> InspectAsync(
        string path,
        IReadOnlyDictionary<string, InstallationRecord> installationsByPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        installationsByPath.TryGetValue(fullPath, out var installation);
        var origin = await GitValueAsync(path, ["remote", "get-url", "origin"], cancellationToken);
        var commit = await GitValueAsync(path, ["rev-parse", "HEAD"], cancellationToken);
        var branch = await GitValueAsync(path, ["branch", "--show-current"], cancellationToken);
        var revision = await GitValueAsync(path, ["describe", "--tags", "--always", "--dirty"], cancellationToken);
        var commitDateValue = await GitValueAsync(path, ["show", "-s", "--format=%cI", "HEAD"], cancellationToken);
        var status = await GitValueAsync(path, ["status", "--porcelain", "--untracked-files=all"], cancellationToken,
            preserveEmpty: true);
        var sourceId = installation?.SourceId ?? ResolveSourceId(origin, Path.GetFileName(path));
        return new CachedRepositoryInfo(
            sourceId,
            RepositoryName(sourceId),
            fullPath,
            origin,
            NullIfEmpty(branch),
            commit,
            revision,
            ParseDate(commitDateValue),
            commit is not null,
            status is null ? null : !string.IsNullOrEmpty(status),
            null,
            installation);
    }

    private async Task<string?> GitValueAsync(
        string path,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool preserveEmpty = false)
    {
        var result = await processes.RunAsync("git", arguments, path, cancellationToken);
        if (!result.Succeeded)
        {
            return null;
        }

        var value = result.StandardOutput.Trim();
        return preserveEmpty || value.Length > 0 ? value : null;
    }

    private string ResolveSourceId(string? origin, string fallback)
        => origin is null ? fallback : sourceParser.NormalizeSourceId(origin);

    private static string RepositoryName(string sourceId)
        => sourceId.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? sourceId;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : null;

    private static long? CalculateSize(string path)
    {
        try
        {
            return CalculateSize(new DirectoryInfo(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long CalculateSize(DirectoryInfo directory)
    {
        var size = directory.EnumerateFiles().Sum(file => file.Length);
        foreach (var child in directory.EnumerateDirectories())
        {
            if (!child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                size += CalculateSize(child);
            }
        }

        return size;
    }

    private static StringComparer PathComparer()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
