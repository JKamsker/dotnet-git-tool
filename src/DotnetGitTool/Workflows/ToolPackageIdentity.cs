using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DotnetGitTool.Workflows;

public static partial class ToolPackageIdentity
{
    public static string GeneratePackageId(string sourceId)
    {
        var safe = InvalidPackageCharacter().Replace(sourceId.Replace('/', '.'), "-").Trim('.', '-');
        var packageId = $"git.{safe}";
        if (packageId.Length <= 100)
        {
            return packageId;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId)))[..12].ToLowerInvariant();
        return $"{packageId[..87]}.{hash}";
    }

    public static string GenerateVersion(string commit, ToolCommandStyle commandStyle)
        => $"0.0.0-git.{ShortCommit(commit).ToLowerInvariant()}.{StyleName(commandStyle)}";

    public static string ShortCommit(string commit) => commit[..Math.Min(12, commit.Length)];

    private static string StyleName(ToolCommandStyle commandStyle)
        => commandStyle == ToolCommandStyle.Dotnet ? "dotnet" : "standalone";

    [GeneratedRegex("[^A-Za-z0-9_.-]+")]
    private static partial Regex InvalidPackageCharacter();
}
