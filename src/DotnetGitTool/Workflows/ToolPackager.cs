using DotnetGitTool.Commands;
using DotnetGitTool.Discovery;
using DotnetGitTool.Output;
using DotnetGitTool.Processes;

namespace DotnetGitTool.Workflows;

public sealed class ToolPackager(IProcessRunner processes, ICliOutput output)
{
    public async Task<PackedTool> PackAsync(
        GlobalSettings settings,
        string repositoryPath,
        ProjectSelection selection,
        string packageId,
        string version,
        string toolCommandName,
        CancellationToken cancellationToken)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"dotnet-git-tool-package-{Guid.NewGuid():N}");
        var packageDirectory = Path.Combine(temporaryRoot, "packages");
        Directory.CreateDirectory(packageDirectory);
        var arguments = new List<string>
        {
            "pack", selection.Project.Path, "--configuration", "Release", "--output", packageDirectory,
            "-p:PackAsTool=true", $"-p:PackageId={packageId}", $"-p:Version={version}",
            $"-p:ToolCommandName={toolCommandName}",
        };

        try
        {
            output.Status(settings, $"Packing {selection.Project.RelativePath}...");
            (await processes.RunAsync("dotnet", arguments, repositoryPath, cancellationToken))
                .EnsureSuccess($"Packing {selection.Project.RelativePath}");
            return new PackedTool(temporaryRoot, packageDirectory);
        }
        catch
        {
            PackedTool.TryDelete(temporaryRoot);
            throw;
        }
    }
}

public sealed class PackedTool(string temporaryRoot, string packageDirectory) : IDisposable
{
    public string PackageDirectory { get; } = packageDirectory;

    public void Dispose() => TryDelete(temporaryRoot);

    internal static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
