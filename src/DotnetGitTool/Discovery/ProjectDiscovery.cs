using System.Text.Json;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Processes;

namespace DotnetGitTool.Discovery;

public sealed class ProjectDiscovery(DotnetProjectRunner dotnet)
{
    public async Task<ProjectSelection> DiscoverAsync(
        string repositoryRoot,
        string? projectOverride,
        CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(repositoryRoot, cancellationToken);
        var selectedPath = ResolveProjectOverride(repositoryRoot, projectOverride ?? manifest?.Project);
        var candidates = selectedPath is null
            ? EnumerateProjects(repositoryRoot).ToArray()
            : [selectedPath];

        if (candidates.Length == 0)
        {
            throw new CliException("No .csproj files were found in the repository.", "project_not_found", ExitCodes.NotFound);
        }

        var projects = new List<ProjectMetadata>(candidates.Length);
        foreach (var candidate in candidates)
        {
            projects.Add(await EvaluateAsync(repositoryRoot, candidate, cancellationToken));
        }

        var selected = Select(projects, selectedPath is not null);
        return new ProjectSelection(selected, manifest?.Command);
    }

    public static ProjectMetadata Select(IReadOnlyList<ProjectMetadata> projects, bool explicitSelection = false)
    {
        if (explicitSelection)
        {
            var project = projects.Single();
            if (!project.IsExecutable && !project.PackAsTool)
            {
                throw new CliException(
                    $"Selected project '{project.RelativePath}' is not executable. Expected OutputType=Exe or PackAsTool=true.",
                    "project_not_executable",
                    ExitCodes.Usage);
            }

            return project;
        }

        var toolProjects = projects.Where(project => project.PackAsTool).ToArray();
        if (toolProjects.Length == 1)
        {
            return toolProjects[0];
        }

        if (toolProjects.Length > 1)
        {
            throw Ambiguous(toolProjects, "PackAsTool projects");
        }

        var executableProjects = projects.Where(project => project.IsExecutable).ToArray();
        if (executableProjects.Length == 1)
        {
            return executableProjects[0];
        }

        if (executableProjects.Length == 0)
        {
            throw new CliException(
                "No executable project was found. Pass --project to select a project explicitly.",
                "project_not_found",
                ExitCodes.NotFound);
        }

        throw Ambiguous(executableProjects, "executable projects");
    }

    private async Task<ProjectMetadata> EvaluateAsync(
        string repositoryRoot,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var result = (await dotnet.RunAsync(
                [
                    "msbuild",
                    projectPath,
                    "--nologo",
                    "-getProperty:OutputType,PackAsTool,AssemblyName,ToolCommandName",
                ],
                repositoryRoot,
                cancellationToken))
            .EnsureSuccess($"Evaluating {Path.GetRelativePath(repositoryRoot, projectPath)}");

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var properties = document.RootElement.GetProperty("Properties");
            var assemblyName = GetString(properties, "AssemblyName") ?? Path.GetFileNameWithoutExtension(projectPath);
            return new ProjectMetadata(
                projectPath,
                Path.GetRelativePath(repositoryRoot, projectPath).Replace(Path.DirectorySeparatorChar, '/'),
                GetString(properties, "OutputType") ?? string.Empty,
                bool.TryParse(GetString(properties, "PackAsTool"), out var packAsTool) && packAsTool,
                assemblyName,
                GetString(properties, "ToolCommandName"));
        }
        catch (JsonException exception)
        {
            throw new CliException(
                $"MSBuild returned an unreadable project evaluation for '{projectPath}': {exception.Message}",
                "project_evaluation_failed");
        }
    }

    private static IEnumerable<string> EnumerateProjects(string root)
        => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !ContainsIgnoredDirectory(root, path))
            .Order(StringComparer.OrdinalIgnoreCase);

    private static bool ContainsIgnoredDirectory(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is ".git" or "bin" or "obj");
    }

    private static string? ResolveProjectOverride(string root, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var rootPath = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(rootPath, value));
        if (!candidate.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) && candidate != rootPath)
        {
            throw new CliException("The selected project must be inside the cloned repository.", "invalid_project", ExitCodes.Usage);
        }

        if (Directory.Exists(candidate))
        {
            var projects = Directory.EnumerateFiles(candidate, "*.csproj", SearchOption.TopDirectoryOnly).ToArray();
            if (projects.Length != 1)
            {
                throw new CliException(
                    $"Directory '{value}' must contain exactly one .csproj file; found {projects.Length}.",
                    "invalid_project",
                    ExitCodes.Usage);
            }

            return projects[0];
        }

        if (!File.Exists(candidate) || !candidate.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException($"Project '{value}' was not found.", "project_not_found", ExitCodes.NotFound);
        }

        return candidate;
    }

    private static async Task<RepositoryManifest?> ReadManifestAsync(string root, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ".config", "dotnet-git-tool.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<RepositoryManifest>(stream, cancellationToken: cancellationToken)
                ?? throw new JsonException("The manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new CliException($"Invalid .config/dotnet-git-tool.json: {exception.Message}", "invalid_manifest", ExitCodes.Usage);
        }
    }

    private static CliException Ambiguous(IEnumerable<ProjectMetadata> projects, string kind)
    {
        var paths = string.Join(", ", projects.Select(project => project.RelativePath));
        return new CliException(
            $"Found multiple {kind}: {paths}. Pass --project <PATH>.",
            "ambiguous_project",
            ExitCodes.Usage);
    }

    private static string? GetString(JsonElement properties, string name)
        => properties.TryGetProperty(name, out var value) ? value.GetString() : null;
}
