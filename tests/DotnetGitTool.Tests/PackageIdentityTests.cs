using DotnetGitTool.Workflows;

namespace DotnetGitTool.Tests;

public sealed class PackageIdentityTests
{
    [Fact]
    public void PackageIdIsStableAndNuGetSafe()
        => Assert.Equal("git.JKamsker.bookmeta-cli", ToolPackageIdentity.GeneratePackageId("JKamsker/bookmeta-cli"));

    [Fact]
    public void PackageIdIsTruncatedDeterministicallyToNuGetLimit()
    {
        var packageId = ToolPackageIdentity.GeneratePackageId($"owner/{new string('r', 150)}");

        Assert.Equal(100, packageId.Length);
        Assert.Equal(packageId, ToolPackageIdentity.GeneratePackageId($"owner/{new string('r', 150)}"));
    }

    [Fact]
    public void VersionsDifferByCommandStyle()
    {
        const string commit = "0123456789abcdef";

        Assert.Equal("0.0.0-git.0123456789ab.dotnet", ToolPackageIdentity.GenerateVersion(commit, ToolCommandStyle.Dotnet));
        Assert.Equal("0.0.0-git.0123456789ab.standalone",
            ToolPackageIdentity.GenerateVersion(commit, ToolCommandStyle.Standalone));
    }
}
