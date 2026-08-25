namespace DotnetGitTool.Source;

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
