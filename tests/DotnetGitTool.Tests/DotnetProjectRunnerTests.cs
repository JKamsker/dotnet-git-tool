using DotnetGitTool.Processes;

namespace DotnetGitTool.Tests;

public sealed class DotnetProjectRunnerTests
{
    private const string SdkFailure = "A compatible SDK was not found. https://aka.ms/dotnet/sdk-not-found";

    [Fact]
    public async Task RetriesOutsideRepositoryWhenANewerSdkIsInstalled()
    {
        using var repository = new TestRepository("10.0.302");
        var processes = new RecordingProcessRunner("10.0.301 [sdk]\n10.0.303 [sdk]\n");
        var runner = new DotnetProjectRunner(processes);

        var result = await runner.RunAsync(
            ["build", "tool.csproj"],
            repository.Path,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(3, processes.Invocations.Count);
        Assert.Equal(repository.Path, processes.Invocations[0].WorkingDirectory);
        Assert.Equal("--list-sdks", Assert.Single(processes.Invocations[1].Arguments));
        Assert.NotEqual(repository.Path, processes.Invocations[2].WorkingDirectory);
    }

    [Fact]
    public async Task DoesNotRetryWhenOnlyAnOlderSdkIsInstalled()
    {
        using var repository = new TestRepository("10.0.302");
        var processes = new RecordingProcessRunner("10.0.301 [sdk]\n");
        var runner = new DotnetProjectRunner(processes);

        var result = await runner.RunAsync(
            ["build", "tool.csproj"],
            repository.Path,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(2, processes.Invocations.Count);
    }

    [Fact]
    public async Task DoesNotRetryAnOrdinaryBuildFailure()
    {
        using var repository = new TestRepository("10.0.302");
        var processes = new RecordingProcessRunner("10.0.303 [sdk]\n", "Compilation failed.");
        var runner = new DotnetProjectRunner(processes);

        var result = await runner.RunAsync(
            ["build", "tool.csproj"],
            repository.Path,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Single(processes.Invocations);
    }

    private sealed class RecordingProcessRunner(string installedSdks, string buildError = SdkFailure) : IProcessRunner
    {
        public List<Invocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string? workingDirectory = null,
            CancellationToken cancellationToken = default)
        {
            var materializedArguments = arguments.ToArray();
            Invocations.Add(new Invocation(materializedArguments, workingDirectory));
            if (materializedArguments.SequenceEqual(["--list-sdks"]))
            {
                return Task.FromResult(new ProcessResult(0, installedSdks, string.Empty));
            }

            var isFallback = workingDirectory is not null &&
                             !workingDirectory.Equals(Invocations[0].WorkingDirectory, StringComparison.Ordinal);
            return Task.FromResult(isFallback
                ? new ProcessResult(0, "Build succeeded.", string.Empty)
                : new ProcessResult(1, string.Empty, buildError));
        }
    }

    private sealed record Invocation(string[] Arguments, string? WorkingDirectory);

    private sealed class TestRepository : IDisposable
    {
        public TestRepository(string sdkVersion)
        {
            Path = Directory.CreateTempSubdirectory("dotnet-git-tool-sdk-tests-").FullName;
            File.WriteAllText(System.IO.Path.Combine(Path, "global.json"), $$"""
                {
                  "sdk": {
                    "version": "{{sdkVersion}}"
                  }
                }
                """);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
