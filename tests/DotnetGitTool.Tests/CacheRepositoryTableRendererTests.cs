using DotnetGitTool.Output;
using DotnetGitTool.Source;
using DotnetGitTool.State;
using Spectre.Console;

namespace DotnetGitTool.Tests;

public sealed class CacheRepositoryTableRendererTests
{
    [Fact]
    public void RendersOnlyCompactColumnsWithSourceVersionAndShortCommit()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        var localDate = new DateTimeOffset(2026, 8, 25, 12, 0, 0, DateTimeOffset.Now.Offset);
        var installation = new InstallationRecord(
            "JKamsker/bookmeta-cli",
            "https://github.com/JKamsker/bookmeta-cli.git",
            null,
            "src/BookMeta.Cli/BookMeta.Cli.csproj",
            "git.JKamsker.bookmeta-cli",
            "0.0.0-git.b9dfcbad6314.dotnet",
            "b9dfcbad63143ed8d26b18761e34efbe38079b2a",
            "dotnet bookmeta",
            "dotnet",
            "/cache/bookmeta",
            localDate);
        var repository = new CachedRepositoryInfo(
            installation.SourceId,
            "bookmeta-cli",
            installation.RepositoryPath!,
            installation.CloneUrl,
            "main",
            installation.Commit,
            "b9dfcba",
            localDate.AddDays(-5),
            true,
            false,
            null,
            installation,
            "1.0.0");
        var inventory = new RepositoryCacheInventory("/cache", [repository]);

        new CacheRepositoryTableRenderer(console).Render(inventory);

        var output = writer.ToString();
        Assert.Contains("Source", output);
        Assert.Contains("Version", output);
        Assert.Contains("Installed at", output);
        Assert.Contains("Published at", output);
        Assert.Contains("JKamsker/bookmeta-cli", output);
        Assert.Contains("[1.0.0|b9dfcbad6314]", output);
        Assert.Contains("25.08.2026", output);
        Assert.Contains("20.08.2026", output);
        Assert.DoesNotContain("/cache/bookmeta", output);
        Assert.DoesNotContain("Managed", output);
    }
}
