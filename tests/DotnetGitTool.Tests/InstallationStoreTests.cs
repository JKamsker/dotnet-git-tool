using DotnetGitTool.State;

namespace DotnetGitTool.Tests;

public sealed class InstallationStoreTests
{
    [Fact]
    public async Task ReadsLegacyInstallationWithoutUpdatedAt()
    {
        var temporaryRoot = Directory.CreateTempSubdirectory("dotnet-git-tool-state-tests-");
        try
        {
            var statePath = Path.Combine(temporaryRoot.FullName, "installed.json");
            await File.WriteAllTextAsync(statePath, """
                {
                  "schemaVersion": 1,
                  "installations": [
                    {
                      "sourceId": "owner/repository",
                      "cloneUrl": "https://github.com/owner/repository.git",
                      "requestedRef": null,
                      "project": "src/tool.csproj",
                      "packageId": "git.owner.repository",
                      "version": "1.2.3",
                      "commit": "0123456789abcdef",
                      "command": "dotnet repository",
                      "commandStyle": "dotnet",
                      "repositoryPath": null,
                      "installedAt": "2026-01-02T03:04:05Z"
                    }
                  ]
                }
                """, TestContext.Current.CancellationToken);
            var store = new InstallationStore(new InstallationStorePath(temporaryRoot.FullName));

            var installation = Assert.Single(
                await store.ListAsync(TestContext.Current.CancellationToken));

            Assert.Equal("owner/repository", installation.SourceId);
            Assert.Equal(DateTimeOffset.Parse("2026-01-02T03:04:05Z"), installation.InstalledAt);
            Assert.Null(installation.UpdatedAt);
        }
        finally
        {
            temporaryRoot.Delete(recursive: true);
        }
    }
}
