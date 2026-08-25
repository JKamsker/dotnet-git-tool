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
}
