using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using DotnetGitTool.Commands;
using DotnetGitTool.Discovery;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Output;
using DotnetGitTool.Processes;
using DotnetGitTool.Source;
using DotnetGitTool.State;

namespace DotnetGitTool.Workflows;

public sealed partial class ToolWorkflow(
    SourceSpecParser sourceParser,
    RepositoryCloner cloner,
    ProjectDiscovery discovery,
    InstallationStore store,
    IProcessRunner processes,
    ICliOutput output)
{
    public async Task<int> InstallAsync(
        MutationSettings settings,
        string sourceValue,
        string? requestedRef,
        string? project,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(settings, async () =>
        {
            var source = sourceParser.Parse(sourceValue, requestedRef);
            var existing = await store.FindAsync(source.SourceId, cancellationToken);
            if (existing is not null)
            {
                throw new CliException(
                    $"'{source.SourceId}' is already managed. Use 'dotnet git-tool update {source.SourceId}'.",
                    "already_installed",
                    ExitCodes.Conflict);
            }

            if (settings.DryRun)
            {
                output.Success(settings,
                    new { action = "install", source = source.Display, project, executesRepositoryCode = true },
                    $"Would clone {source.Display}, discover {project ?? "a tool project"}, pack it, and install it globally.");
                return ExitCodes.Success;
            }

            InteractionGuard.ConfirmCodeExecution(settings, source.Display);
            output.Diagnostic(settings, $"Clone URL: {source.CloneUrl}");
            output.Status(settings, $"Cloning {source.Display}...");
            using var repository = await cloner.CloneAsync(source, cancellationToken);
            output.Diagnostic(settings, $"Resolved commit: {repository.Commit}");
            output.Status(settings, "Discovering executable projects with MSBuild...");
            var selection = await discovery.DiscoverAsync(repository.Path, project, cancellationToken);
            var packageId = GeneratePackageId(source.SourceId);
            var version = GenerateVersion(repository.Commit);
            var command = selection.CommandName;
            output.Diagnostic(settings, $"Selected project: {selection.Project.RelativePath}");
            output.Diagnostic(settings, $"Generated package: {packageId} {version}; command: {command}");
            var packageDirectory = await PackAsync(settings, repository, selection, packageId, version, cancellationToken);

            output.Status(settings, $"Installing {packageId} {version} globally...");
            (await processes.RunAsync("dotnet",
                    ["tool", "install", "--global", packageId, "--version", version, "--add-source", packageDirectory,
                        "--ignore-failed-sources"],
                    cancellationToken: cancellationToken))
                .EnsureSuccess($"Installing {packageId}");

            var record = new InstallationRecord(
                source.SourceId,
                source.CloneUrl,
                source.RequestedRef,
                selection.Project.RelativePath,
                packageId,
                version,
                repository.Commit,
                command,
                DateTimeOffset.UtcNow);
            try
            {
                await store.AddAsync(record, cancellationToken);
            }
            catch
            {
                await processes.RunAsync("dotnet", ["tool", "uninstall", "--global", packageId],
                    cancellationToken: CancellationToken.None);
                throw;
            }

            output.Success(settings,
                new { action = "installed", installation = record },
                $"Installed {source.SourceId} at {ShortCommit(repository.Commit)}. Command: {command}");
            return ExitCodes.Success;
        });
    }

    public async Task<int> UpdateAsync(
        MutationSettings settings,
        string sourceValue,
        string? requestedRef,
        string? project,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(settings, async () =>
        {
            var requested = sourceParser.Parse(sourceValue, requestedRef);
            var installed = await store.FindAsync(requested.SourceId, cancellationToken)
                ?? throw new CliException(
                    $"'{requested.SourceId}' is not managed. Install it first with 'dotnet git-tool install {sourceValue}'.",
                    "installation_not_found",
                    ExitCodes.NotFound);
            var source = new SourceSpec(
                installed.CloneUrl,
                installed.SourceId,
                requested.RequestedRef ?? installed.RequestedRef);
            var selectedProject = project ?? installed.Project;

            if (settings.DryRun)
            {
                output.Success(settings,
                    new { action = "update", source = source.Display, project = selectedProject, executesRepositoryCode = true },
                    $"Would clone {source.Display}, rebuild {selectedProject}, and update {installed.PackageId} globally.");
                return ExitCodes.Success;
            }

            InteractionGuard.ConfirmCodeExecution(settings, source.Display);
            output.Diagnostic(settings, $"Clone URL: {source.CloneUrl}");
            output.Status(settings, $"Cloning {source.Display}...");
            using var repository = await cloner.CloneAsync(source, cancellationToken);
            output.Diagnostic(settings, $"Resolved commit: {repository.Commit}");
            if (repository.Commit.Equals(installed.Commit, StringComparison.OrdinalIgnoreCase))
            {
                output.Success(settings,
                    new { action = "unchanged", installation = installed },
                    $"{installed.SourceId} is already at {ShortCommit(installed.Commit)}.");
                return ExitCodes.Success;
            }

            var selection = await discovery.DiscoverAsync(repository.Path, selectedProject, cancellationToken);
            var version = GenerateVersion(repository.Commit);
            var command = selection.CommandName;
            output.Diagnostic(settings, $"Selected project: {selection.Project.RelativePath}");
            output.Diagnostic(settings, $"Generated package: {installed.PackageId} {version}; command: {command}");
            var packageDirectory = await PackAsync(settings, repository, selection, installed.PackageId, version, cancellationToken);

            output.Status(settings, $"Updating {installed.PackageId} to {version}...");
            (await processes.RunAsync("dotnet",
                    ["tool", "update", "--global", installed.PackageId, "--version", version, "--add-source", packageDirectory,
                        "--ignore-failed-sources", "--allow-downgrade"],
                    cancellationToken: cancellationToken))
                .EnsureSuccess($"Updating {installed.PackageId}");

            var record = installed with
            {
                RequestedRef = source.RequestedRef,
                Project = selection.Project.RelativePath,
                Version = version,
                Commit = repository.Commit,
                Command = command,
                InstalledAt = DateTimeOffset.UtcNow,
            };
            await store.ReplaceAsync(record, cancellationToken);
            output.Success(settings,
                new { action = "updated", installation = record },
                $"Updated {record.SourceId} to {ShortCommit(record.Commit)}. Command: {command}");
            return ExitCodes.Success;
        });
    }

    public async Task<int> UninstallAsync(
        MutationSettings settings,
        string sourceValue,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(settings, async () =>
        {
            var sourceId = sourceParser.NormalizeSourceId(sourceValue);
            var installed = await store.FindAsync(sourceId, cancellationToken)
                ?? throw new CliException($"'{sourceId}' is not managed by dotnet git-tool.", "installation_not_found", ExitCodes.NotFound);

            if (settings.DryRun)
            {
                output.Success(settings,
                    new { action = "uninstall", installation = installed },
                    $"Would uninstall {installed.PackageId} and remove the record for {installed.SourceId}.");
                return ExitCodes.Success;
            }

            InteractionGuard.ConfirmUninstall(settings, installed.SourceId);
            output.Status(settings, $"Uninstalling {installed.PackageId}...");
            (await processes.RunAsync("dotnet", ["tool", "uninstall", "--global", installed.PackageId],
                    cancellationToken: cancellationToken))
                .EnsureSuccess($"Uninstalling {installed.PackageId}");
            await store.RemoveAsync(installed.SourceId, cancellationToken);
            output.Success(settings,
                new { action = "uninstalled", installation = installed },
                $"Uninstalled {installed.SourceId} ({installed.PackageId}).");
            return ExitCodes.Success;
        });
    }

    private async Task<string> PackAsync(
        GlobalSettings settings,
        ClonedRepository repository,
        ProjectSelection selection,
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        var packageDirectory = Path.Combine(Path.GetDirectoryName(repository.Path)!, "packages");
        Directory.CreateDirectory(packageDirectory);
        var arguments = new List<string>
        {
            "pack", selection.Project.Path, "--configuration", "Release", "--output", packageDirectory,
            $"-p:PackAsTool=true", $"-p:PackageId={packageId}", $"-p:Version={version}",
        };
        if (!string.IsNullOrWhiteSpace(selection.CommandOverride))
        {
            arguments.Add($"-p:ToolCommandName={selection.CommandOverride}");
        }

        output.Status(settings, $"Packing {selection.Project.RelativePath}...");
        (await processes.RunAsync("dotnet", arguments, repository.Path, cancellationToken))
            .EnsureSuccess($"Packing {selection.Project.RelativePath}");
        return packageDirectory;
    }

    private async Task<int> ExecuteAsync(GlobalSettings settings, Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (CliException exception)
        {
            output.Failure(settings, exception);
            return exception.ExitCode;
        }
        catch (OperationCanceledException)
        {
            var exception = new CliException("Operation cancelled.", "cancelled", ExitCodes.Cancelled);
            output.Failure(settings, exception);
            return exception.ExitCode;
        }
        catch (Exception exception)
        {
            var failure = new CliException(exception.Message, "unexpected_error");
            output.Failure(settings, failure);
            return failure.ExitCode;
        }
    }

    public static string GeneratePackageId(string sourceId)
    {
        var safe = InvalidPackageCharacter().Replace(sourceId.Replace('/', '.'), "-").Trim('.', '-');
        var packageId = $"git.{safe}";
        if (packageId.Length <= 100)
        {
            return packageId;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId)))[..12].ToLowerInvariant();
        return $"{packageId[..87]}.{hash}";
    }

    private static string GenerateVersion(string commit) => $"0.0.0-git.{ShortCommit(commit).ToLowerInvariant()}";

    private static string ShortCommit(string commit) => commit[..Math.Min(12, commit.Length)];

    [GeneratedRegex("[^A-Za-z0-9_.-]+")]
    private static partial Regex InvalidPackageCharacter();
}
