namespace DotnetGitTool.Discovery;

public sealed record ProjectMetadata(
    string Path,
    string RelativePath,
    string OutputType,
    bool PackAsTool,
    string AssemblyName,
    string? ToolCommandName)
{
    public bool IsExecutable => OutputType.Equals("Exe", StringComparison.OrdinalIgnoreCase);
}

internal sealed record RepositoryManifest(string? Project, string? Command);

public sealed record ProjectSelection(ProjectMetadata Project, string? CommandOverride);
