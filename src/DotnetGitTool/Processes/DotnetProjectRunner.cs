using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotnetGitTool.Processes;

public sealed partial class DotnetProjectRunner(IProcessRunner processes)
{
    private const string SdkNotFoundHelpUrl = "https://aka.ms/dotnet/sdk-not-found";

    public async Task<ProcessResult> RunAsync(
        IEnumerable<string> arguments,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var materializedArguments = arguments.ToArray();
        var result = await processes.RunAsync("dotnet", materializedArguments, repositoryPath, cancellationToken);
        if (result.Succeeded || !IsSdkResolutionFailure(result))
        {
            return result;
        }

        var requestedSdk = await ReadRequestedSdkAsync(repositoryPath, cancellationToken);
        if (requestedSdk is null || !await HasNewerInstalledSdkAsync(requestedSdk, cancellationToken))
        {
            return result;
        }

        var fallbackDirectory = Directory.CreateTempSubdirectory("dotnet-git-tool-sdk-");
        try
        {
            return await processes.RunAsync(
                "dotnet",
                materializedArguments,
                fallbackDirectory.FullName,
                cancellationToken);
        }
        finally
        {
            TryDelete(fallbackDirectory.FullName);
        }
    }

    private async Task<bool> HasNewerInstalledSdkAsync(Version requestedSdk, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("dotnet", ["--list-sdks"], cancellationToken: cancellationToken);
        if (!result.Succeeded)
        {
            return false;
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseInstalledSdk)
            .Any(installedSdk => installedSdk is not null && installedSdk > requestedSdk);
    }

    private static async Task<Version?> ReadRequestedSdkAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var globalJsonPath = Path.Combine(repositoryPath, "global.json");
        if (!File.Exists(globalJsonPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(globalJsonPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("sdk", out var sdk) ||
                !sdk.TryGetProperty("version", out var versionProperty))
            {
                return null;
            }

            var version = versionProperty.GetString();
            return ParseVersion(version);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Version? ParseInstalledSdk(string line)
    {
        var match = InstalledSdkLine().Match(line);
        return match.Success ? ParseVersion(match.Groups["version"].Value) : null;
    }

    private static Version? ParseVersion(string? value)
    {
        var numericVersion = value?.Split('-', 2)[0];
        return Version.TryParse(numericVersion, out var version) ? version : null;
    }

    private static bool IsSdkResolutionFailure(ProcessResult result)
        => result.StandardError.Contains(SdkNotFoundHelpUrl, StringComparison.OrdinalIgnoreCase) ||
           result.StandardOutput.Contains(SdkNotFoundHelpUrl, StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex("^(?<version>\\d+\\.\\d+\\.\\d+(?:-[^\\s]+)?)\\s+\\[")]
    private static partial Regex InstalledSdkLine();
}
