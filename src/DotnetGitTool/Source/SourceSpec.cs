using System.Text.RegularExpressions;
using DotnetGitTool.Infrastructure;

namespace DotnetGitTool.Source;

public sealed record SourceSpec(string CloneUrl, string SourceId, string? RequestedRef)
{
    public string Display => RequestedRef is null ? SourceId : $"{SourceId}@{RequestedRef}";
}

public sealed partial class SourceSpecParser
{
    public SourceSpec Parse(string value, string? explicitRef = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException("A repository is required.", "invalid_source", ExitCodes.Usage);
        }

        var input = value.Trim();
        var slugMatch = GitHubSlug().Match(input);
        if (slugMatch.Success)
        {
            var owner = slugMatch.Groups["owner"].Value;
            var repository = TrimGitSuffix(slugMatch.Groups["repo"].Value);
            var requestedRef = ValidateRef(explicitRef ?? NullIfEmpty(slugMatch.Groups["ref"].Value));
            return new SourceSpec(
                $"https://github.com/{owner}/{repository}.git",
                $"{owner}/{repository}",
                requestedRef);
        }

        var sshMatch = GitHubSsh().Match(input);
        if (sshMatch.Success)
        {
            var owner = sshMatch.Groups["owner"].Value;
            var repository = TrimGitSuffix(sshMatch.Groups["repo"].Value);
            return new SourceSpec(input, $"{owner}/{repository}", ValidateRef(explicitRef));
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase)))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                throw new CliException("Repository URLs must include an owner and repository path.", "invalid_source", ExitCodes.Usage);
            }

            var repository = TrimGitSuffix(segments[^1]);
            var owner = segments[^2];
            var sourceId = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? $"{owner}/{repository}"
                : $"{uri.Host}/{owner}/{repository}";
            return new SourceSpec(input, sourceId, ValidateRef(explicitRef));
        }

        throw new CliException(
            "Repository must be an owner/repo GitHub slug or an HTTP(S)/SSH Git URL.",
            "invalid_source",
            ExitCodes.Usage);
    }

    public string NormalizeSourceId(string value)
    {
        try
        {
            return Parse(value).SourceId;
        }
        catch (CliException)
        {
            return value.Trim();
        }
    }

    private static string TrimGitSuffix(string value)
        => value.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? ValidateRef(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length > 1024 || value.StartsWith('-') || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
        {
            throw new CliException("The requested ref is not a valid branch, tag, or commit name.", "invalid_ref", ExitCodes.Usage);
        }

        return value;
    }

    [GeneratedRegex("^(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+?)(?:@(?<ref>[^\\s]+))?$")]
    private static partial Regex GitHubSlug();

    [GeneratedRegex("^git@github\\.com:(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+?)(?:\\.git)?$")]
    private static partial Regex GitHubSsh();
}
