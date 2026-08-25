using System.Diagnostics;
using System.Text.Json;
using DotnetGitTool.Source;
using DotnetGitTool.State;

namespace DotnetGitTool.Tests;

public sealed class UpdateRefSelectionTests
{
    [Theory]
    [InlineData("install")]
    [InlineData("update")]
    public async Task ManagedToolCanSwitchToEmbeddedRef(string command)
    {
        using var environment = await ManagedToolEnvironment.CreateAsync("main");

        var result = await RunAsync(
            [command, "owner/repository@f589ee1", "--dry-run", "--json"],
            environment.Variables);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("update", data.GetProperty("action").GetString());
        Assert.Equal("owner/repository@f589ee1", data.GetProperty("source").GetString());
        Assert.Equal("standalone", data.GetProperty("commandStyle").GetString());
    }

    [Fact]
    public async Task UpdateWithoutRefReturnsPinnedToolToDefaultBranch()
    {
        using var environment = await ManagedToolEnvironment.CreateAsync("f589ee1");

        var result = await RunAsync(
            ["update", "owner/repository", "--dry-run", "--json"],
            environment.Variables);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("update", data.GetProperty("action").GetString());
        Assert.Equal("owner/repository", data.GetProperty("source").GetString());
    }

    private static async Task<CliProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment)
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

        foreach (var item in environment)
        {
            startInfo.Environment[item.Key] = item.Value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the CLI process.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new CliProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class ManagedToolEnvironment : IDisposable
    {
        private readonly DirectoryInfo temporaryRoot =
            Directory.CreateTempSubdirectory("dotnet-git-tool-switch-ref-tests-");

        private ManagedToolEnvironment()
        {
            Variables = new Dictionary<string, string>
            {
                ["DOTNET_GIT_TOOL_HOME"] = Path.Combine(temporaryRoot.FullName, "state"),
                ["DOTNET_GIT_TOOL_CACHE"] = Path.Combine(temporaryRoot.FullName, "cache"),
            };
        }

        public IReadOnlyDictionary<string, string> Variables { get; }

        public static async Task<ManagedToolEnvironment> CreateAsync(string requestedRef)
        {
            var environment = new ManagedToolEnvironment();
            var store = new InstallationStore(new InstallationStorePath(
                environment.Variables["DOTNET_GIT_TOOL_HOME"]));
            await store.AddAsync(
                new InstallationRecord(
                    "owner/repository",
                    "https://github.com/owner/repository.git",
                    requestedRef,
                    "src/tool.csproj",
                    "git.owner.repository",
                    "0.0.0-git.0123456789ab.standalone",
                    "0123456789abcdef",
                    "repository",
                    "standalone",
                    null,
                    DateTimeOffset.Parse("2026-01-02T03:04:05Z")),
                TestContext.Current.CancellationToken);
            return environment;
        }

        public void Dispose() => temporaryRoot.Delete(recursive: true);
    }
}
