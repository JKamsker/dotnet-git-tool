using System.Diagnostics;
using System.Text.Json;
using DotnetGitTool.Source;

namespace DotnetGitTool.Tests;

public sealed class CliProcessTests
{
    [Fact]
    public async Task InstallHelpIsSuccessfulAndShowsSafetyFlags()
    {
        var result = await RunAsync(["install", "--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--dry-run", result.StandardOutput);
        Assert.Contains("--yes", result.StandardOutput);
        Assert.Contains("--standalone", result.StandardOutput);
        Assert.Contains("--dotnet-command", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task JsonModeRefusesImplicitConfirmationWithStructuredError()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("dotnet-git-tool-tests-");
        try
        {
            var result = await RunAsync(
                ["install", "owner/repository", "--json"],
                new Dictionary<string, string> { ["DOTNET_GIT_TOOL_HOME"] = stateDirectory.FullName });

            Assert.Equal(2, result.ExitCode);
            Assert.Empty(result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("confirmation_required",
                document.RootElement.GetProperty("error").GetProperty("kind").GetString());
        }
        finally
        {
            stateDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task CommandStyleFlagsAreMutuallyExclusiveUsageError()
    {
        var result = await RunAsync(
            ["install", "owner/repository", "--dry-run", "--standalone", "--dotnet-command"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("cannot be used together", result.StandardOutput + result.StandardError);
    }

    [Fact]
    public async Task DefaultDryRunReportsDotnetCommandStyleInJson()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("dotnet-git-tool-tests-");
        try
        {
            var result = await RunAsync(
                ["install", "owner/repository", "--dry-run", "--json"],
                new Dictionary<string, string> { ["DOTNET_GIT_TOOL_HOME"] = stateDirectory.FullName });

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("dotnet",
                document.RootElement.GetProperty("data").GetProperty("commandStyle").GetString());
        }
        finally
        {
            stateDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task CachePruneHelpIsSuccessfulAndShowsSafetyFlags()
    {
        var result = await RunAsync(["cache", "prune", "--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--dry-run", result.StandardOutput);
        Assert.Contains("--yes", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task CachePruneDryRunJsonListsUnusedRepositoryWithoutDeletingIt()
    {
        using var environment = new CachePruneEnvironment();

        var result = await RunAsync(
            ["cache", "prune", "--dry-run", "--json"],
            environment.Variables);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.True(Directory.Exists(environment.UnusedRepositoryPath));
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("cache_prune_preview", data.GetProperty("action").GetString());
        Assert.Equal(environment.UnusedRepositoryPath,
            data.GetProperty("unusedRepositoryPaths")[0].GetString());
    }

    [Fact]
    public async Task CachePruneJsonRefusesDeletionWithoutYes()
    {
        using var environment = new CachePruneEnvironment();

        var result = await RunAsync(["cache", "prune", "--json"], environment.Variables);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.True(Directory.Exists(environment.UnusedRepositoryPath));
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("confirmation_required",
            document.RootElement.GetProperty("error").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task CachePruneYesJsonDeletesUnusedRepository()
    {
        using var environment = new CachePruneEnvironment();

        var result = await RunAsync(["cache", "prune", "--yes", "--json"], environment.Variables);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.False(Directory.Exists(environment.UnusedRepositoryPath));
        using var document = JsonDocument.Parse(result.StandardOutput);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("cache_pruned", data.GetProperty("action").GetString());
        Assert.Equal(environment.UnusedRepositoryPath,
            data.GetProperty("removedRepositoryPaths")[0].GetString());
    }

    private static async Task<CliProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(SourceSpecParser).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the CLI process.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new CliProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class CachePruneEnvironment : IDisposable
    {
        private readonly DirectoryInfo temporaryRoot =
            Directory.CreateTempSubdirectory("dotnet-git-tool-cli-prune-tests-");

        public CachePruneEnvironment()
        {
            var cachePath = Path.Combine(temporaryRoot.FullName, "cache");
            UnusedRepositoryPath = Path.Combine(cachePath, "repositories", "owner-unused-0123456789ab");
            Directory.CreateDirectory(UnusedRepositoryPath);
            Variables = new Dictionary<string, string>
            {
                ["DOTNET_GIT_TOOL_CACHE"] = cachePath,
                ["DOTNET_GIT_TOOL_HOME"] = Path.Combine(temporaryRoot.FullName, "state"),
            };
        }

        public string UnusedRepositoryPath { get; }
        public IReadOnlyDictionary<string, string> Variables { get; }

        public void Dispose() => temporaryRoot.Delete(recursive: true);
    }
}
