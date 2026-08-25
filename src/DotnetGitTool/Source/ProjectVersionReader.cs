using System.Xml;
using System.Xml.Linq;

namespace DotnetGitTool.Source;

public sealed class ProjectVersionReader
{
    public string? Read(string repositoryPath, string project)
    {
        var projectPath = Path.GetFullPath(Path.Combine(repositoryPath, project));
        var repositoryRoot = Path.GetFullPath(repositoryPath);
        if (!IsInside(repositoryRoot, projectPath) ||
            !File.Exists(projectPath) ||
            ContainsReparsePoint(repositoryRoot, projectPath))
        {
            return null;
        }

        var version = ReadVersionProperty(projectPath);
        var directory = Path.GetDirectoryName(projectPath);
        while (version is null && directory is not null && IsInside(repositoryRoot, directory))
        {
            version = ReadVersionProperty(Path.Combine(directory, "Directory.Build.props"));
            if (PathComparer().Equals(directory, repositoryRoot))
            {
                break;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return version;
    }

    private static string? ReadVersionProperty(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var file = new FileInfo(path);
        if (file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return null;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(path, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            foreach (var propertyName in new[] { "PackageVersion", "Version", "VersionPrefix" })
            {
                var value = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName.Equals(
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                    ?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(value) && !value.Contains("$(", StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
        }

        return null;
    }

    private static bool ContainsReparsePoint(string root, string path)
    {
        var file = new FileInfo(path);
        if (file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        var directory = file.Directory;
        while (directory is not null && IsInside(root, directory.FullName))
        {
            if (directory.LinkTarget is not null || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            if (PathComparer().Equals(root, directory.FullName))
            {
                break;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static bool IsInside(string root, string path)
        => PathComparer().Equals(root, path) ||
           path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison());

    private static StringComparer PathComparer()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
