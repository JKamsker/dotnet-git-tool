using DotnetGitTool.State;
using DotnetGitTool.Workflows;

namespace DotnetGitTool.Tests;

public sealed class ToolCommandIdentityTests
{
    [Fact]
    public void DotnetStyleAddsPackagePrefixAndUsesSubcommandInvocation()
    {
        var command = ToolCommandIdentity.Create("bookmeta", ToolCommandStyle.Dotnet);

        Assert.Equal("bookmeta", command.BaseName);
        Assert.Equal("dotnet-bookmeta", command.PackageCommand);
        Assert.Equal("dotnet bookmeta", command.Invocation);
        Assert.Equal("dotnet", command.StyleName);
    }

    [Fact]
    public void StandaloneStyleRemovesExistingDotnetPrefix()
    {
        var command = ToolCommandIdentity.Create("dotnet-bookmeta", ToolCommandStyle.Standalone);

        Assert.Equal("bookmeta", command.PackageCommand);
        Assert.Equal("bookmeta", command.Invocation);
    }

    [Fact]
    public void LegacyInstallationWithoutStyleIsInferredAsStandalone()
    {
        var installation = new InstallationRecord(
            "owner/repository",
            "https://github.com/owner/repository.git",
            null,
            "tool.csproj",
            "git.owner.repository",
            "0.0.0-git.0123456789ab",
            "0123456789abcdef",
            "bookmeta",
            null,
            null,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(ToolCommandStyle.Standalone, ToolCommandIdentity.InferInstalledStyle(installation));
    }
}
