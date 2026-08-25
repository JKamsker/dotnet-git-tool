using DotnetGitTool.Workflows;

namespace DotnetGitTool.Tests;

public sealed class PackageIdentityTests
{
    [Fact]
    public void PackageIdIsStableAndNuGetSafe()
        => Assert.Equal("git.JKamsker.bookmeta-cli", ToolWorkflow.GeneratePackageId("JKamsker/bookmeta-cli"));

    [Fact]
    public void PackageIdIsTruncatedDeterministicallyToNuGetLimit()
    {
        var packageId = ToolWorkflow.GeneratePackageId($"owner/{new string('r', 150)}");

        Assert.Equal(100, packageId.Length);
        Assert.Equal(packageId, ToolWorkflow.GeneratePackageId($"owner/{new string('r', 150)}"));
    }
}
