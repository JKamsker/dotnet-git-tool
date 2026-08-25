using DotnetGitTool.Discovery;
using DotnetGitTool.Infrastructure;

namespace DotnetGitTool.Tests;

public sealed class ProjectDiscoveryTests
{
    [Fact]
    public async Task EvaluatesAnOrdinaryConsoleProjectWithMsBuild()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/Fixtures/SimpleTool"));
        var discovery = new ProjectDiscovery(new Processes.ProcessRunner());

        var selection = await discovery.DiscoverAsync(
            root,
            "SimpleTool.csproj",
            TestContext.Current.CancellationToken);

        Assert.True(selection.Project.IsExecutable);
        Assert.False(selection.Project.PackAsTool);
        Assert.Equal("fixture-command", selection.Project.AssemblyName);
    }

    [Fact]
    public void PrefersSinglePackAsToolProject()
    {
        var executable = Project("app.csproj", packAsTool: false, outputType: "Exe");
        var tool = Project("tool.csproj", packAsTool: true, outputType: "Exe");

        var selected = ProjectDiscovery.Select([executable, tool]);

        Assert.Same(tool, selected);
    }

    [Fact]
    public void FallsBackToSingleExecutableProject()
    {
        var library = Project("lib.csproj", packAsTool: false, outputType: "Library");
        var executable = Project("app.csproj", packAsTool: false, outputType: "Exe");

        var selected = ProjectDiscovery.Select([library, executable]);

        Assert.Same(executable, selected);
    }

    [Fact]
    public void RequiresOverrideForMultipleExecutables()
    {
        var exception = Assert.Throws<CliException>(() => ProjectDiscovery.Select(
            [Project("one.csproj", false, "Exe"), Project("two.csproj", false, "Exe")]));

        Assert.Equal("ambiguous_project", exception.Kind);
        Assert.Equal(2, exception.ExitCode);
    }

    private static ProjectMetadata Project(string path, bool packAsTool, string outputType)
        => new(path, path, outputType, packAsTool, Path.GetFileNameWithoutExtension(path), null);
}
