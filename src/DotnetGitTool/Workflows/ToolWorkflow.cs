using DotnetGitTool.Commands;
using DotnetGitTool.Discovery;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Output;
using DotnetGitTool.Processes;
using DotnetGitTool.Source;
using DotnetGitTool.State;

namespace DotnetGitTool.Workflows;

public sealed class ToolWorkflow(
    SourceSpecParser sourceParser,
    RepositoryCache repositoryCache,
    ProjectDiscovery discovery,
    InstallationStore store,
    ToolPackager packager,
    IProcessRunner processes,
    ICliOutput output,
    WorkflowExecution execution)
{
    public async Task<int> InstallAsync(
        ToolCommandSettings settings,
        string sourceValue,
        string? requestedRef,
        string? project,
        CancellationToken cancellationToken = default)
    {
        return await execution.RunAsync(settings, async () =>
        {
            var source = sourceParser.Parse(sourceValue, requestedRef);
            var existing = await store.FindAsync(source.SourceId, cancellationToken);
            if (existing is not null)
            {
                return await UpdateExistingAsync(
                    settings,
                    existing,
                    source.RequestedRef ?? existing.RequestedRef,
                    project,
                    cancellationToken);
            }

            var commandStyle = settings.ResolveCommandStyleOverride() ?? ToolCommandStyle.Dotnet;
            if (settings.DryRun)
            {
                var repositoryPath = repositoryCache.GetRepositoryPath(source);
                output.Success(settings,
                    new
                    {
                        action = "install",
                        source = source.Display,
                        project,
                        commandStyle = StyleName(commandStyle),
                        repositoryPath,
                        repositoryCached = Directory.Exists(Path.Combine(repositoryPath, ".git")),
                        executesRepositoryCode = true,
                    },
                    $"Would prepare cached sources for {source.Display}, discover {project ?? "a tool project"}, pack it for " +
                    $"{StyleDescription(commandStyle)}, install it globally, and retain clean sources at {repositoryPath}.");
                return ExitCodes.Success;
            }

            InteractionGuard.ConfirmCodeExecution(settings, source.Display);
            output.Diagnostic(settings, $"Clone URL: {source.CloneUrl}");
            output.Status(settings, $"Preparing cached repository for {source.Display}...");
            await using var repository = await repositoryCache.PrepareAsync(source, cancellationToken);
            output.Diagnostic(settings, $"Repository cache: {repository.Path}");
            output.Diagnostic(settings, $"Resolved commit: {repository.Commit}");
            output.Status(settings, "Discovering executable projects with MSBuild...");
            var selection = await discovery.DiscoverAsync(repository.Path, project, cancellationToken);
            var packageId = ToolPackageIdentity.GeneratePackageId(source.SourceId);
            var version = ToolPackageIdentity.GenerateVersion(repository.Commit, commandStyle);
            var command = ToolCommandIdentity.Create(selection.CommandName, commandStyle);
            output.Diagnostic(settings, $"Selected project: {selection.Project.RelativePath}");
            output.Diagnostic(settings,
                $"Generated package: {packageId} {version}; tool command: {command.PackageCommand}; invocation: {command.Invocation}");
            using var package = await packager.PackAsync(
                settings,
                repository.Path,
                selection,
                packageId,
                version,
                command.PackageCommand,
                cancellationToken);
            await repository.CleanAsync(CancellationToken.None);

            output.Status(settings, $"Installing {packageId} {version} globally...");
            (await processes.RunAsync("dotnet",
                    ["tool", "install", "--global", packageId, "--version", version, "--add-source", package.PackageDirectory,
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
                command.Invocation,
                command.StyleName,
                repository.Path,
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
                $"Installed {source.SourceId} at {ToolPackageIdentity.ShortCommit(repository.Commit)}. " +
                $"Command: {command.Invocation}. Clean sources: {repository.Path}");
            return ExitCodes.Success;
        });
    }

    public async Task<int> UpdateAsync(
        ToolCommandSettings settings,
        string sourceValue,
        string? requestedRef,
        string? project,
        CancellationToken cancellationToken = default)
    {
        return await execution.RunAsync(settings, async () =>
        {
            var commandStyleOverride = settings.ResolveCommandStyleOverride();
            var requested = sourceParser.Parse(sourceValue, requestedRef);
            var installed = await store.FindAsync(requested.SourceId, cancellationToken)
                ?? throw new CliException(
                    $"'{requested.SourceId}' is not managed. Install it first with 'dotnet git-tool install {sourceValue}'.",
                    "installation_not_found",
                    ExitCodes.NotFound);
            return await UpdateExistingAsync(
                settings,
                installed,
                requested.RequestedRef,
                project,
                cancellationToken,
                commandStyleOverride);
        });
    }

    private async Task<int> UpdateExistingAsync(
        ToolCommandSettings settings,
        InstallationRecord installed,
        string? requestedRef,
        string? project,
        CancellationToken cancellationToken,
        ToolCommandStyle? commandStyleOverride = null)
    {
        commandStyleOverride ??= settings.ResolveCommandStyleOverride();
        var source = new SourceSpec(
            installed.CloneUrl,
            installed.SourceId,
            requestedRef);
        var selectedProject = project ?? installed.Project;
        var installedStyle = ToolCommandIdentity.InferInstalledStyle(installed);
        var commandStyle = commandStyleOverride ?? installedStyle;

        if (settings.DryRun)
        {
            var repositoryPath = repositoryCache.GetRepositoryPath(source);
            output.Success(settings,
                new
                {
                    action = "update",
                    source = source.Display,
                    project = selectedProject,
                    commandStyle = StyleName(commandStyle),
                    repositoryPath,
                    repositoryCached = Directory.Exists(Path.Combine(repositoryPath, ".git")),
                    executesRepositoryCode = true,
                },
                $"Would refresh cached sources for {source.Display}, rebuild {selectedProject} for {StyleDescription(commandStyle)}, " +
                $"update {installed.PackageId} globally, and retain clean sources at {repositoryPath}.");
            return ExitCodes.Success;
        }

        InteractionGuard.ConfirmCodeExecution(settings, source.Display);
        output.Diagnostic(settings, $"Clone URL: {source.CloneUrl}");
        output.Status(settings, $"Refreshing cached repository for {source.Display}...");
        await using var repository = await repositoryCache.PrepareAsync(source, cancellationToken);
        output.Diagnostic(settings, $"Repository cache: {repository.Path}");
        output.Diagnostic(settings, $"Resolved commit: {repository.Commit}");
        if (repository.Commit.Equals(installed.Commit, StringComparison.OrdinalIgnoreCase) && commandStyle == installedStyle)
        {
            var unchangedRecord = installed with
            {
                RequestedRef = source.RequestedRef,
                CommandStyle = StyleName(installedStyle),
                RepositoryPath = repository.Path,
            };
            await store.ReplaceAsync(unchangedRecord, cancellationToken);
            output.Success(settings,
                new { action = "unchanged", installation = unchangedRecord },
                $"{installed.SourceId} is already at {ToolPackageIdentity.ShortCommit(installed.Commit)}. " +
                $"Clean sources: {repository.Path}");
            return ExitCodes.Success;
        }

        var selection = await discovery.DiscoverAsync(repository.Path, selectedProject, cancellationToken);
        var version = ToolPackageIdentity.GenerateVersion(repository.Commit, commandStyle);
        var command = ToolCommandIdentity.Create(selection.CommandName, commandStyle);
        output.Diagnostic(settings, $"Selected project: {selection.Project.RelativePath}");
        output.Diagnostic(settings,
            $"Generated package: {installed.PackageId} {version}; tool command: {command.PackageCommand}; " +
            $"invocation: {command.Invocation}");
        using var package = await packager.PackAsync(
            settings,
            repository.Path,
            selection,
            installed.PackageId,
            version,
            command.PackageCommand,
            cancellationToken);
        await repository.CleanAsync(CancellationToken.None);

        output.Status(settings, $"Updating {installed.PackageId} to {version}...");
        (await processes.RunAsync("dotnet",
                ["tool", "update", "--global", installed.PackageId, "--version", version, "--add-source", package.PackageDirectory,
                    "--ignore-failed-sources", "--allow-downgrade"],
                cancellationToken: cancellationToken))
            .EnsureSuccess($"Updating {installed.PackageId}");

        var record = installed with
        {
            RequestedRef = source.RequestedRef,
            Project = selection.Project.RelativePath,
            Version = version,
            Commit = repository.Commit,
            Command = command.Invocation,
            CommandStyle = command.StyleName,
            RepositoryPath = repository.Path,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await store.ReplaceAsync(record, cancellationToken);
        output.Success(settings,
            new { action = "updated", installation = record },
            $"Updated {record.SourceId} to {ToolPackageIdentity.ShortCommit(record.Commit)}. " +
            $"Command: {command.Invocation}. Clean sources: {repository.Path}");
        return ExitCodes.Success;
    }

    public async Task<int> UninstallAsync(
        MutationSettings settings,
        string sourceValue,
        CancellationToken cancellationToken = default)
    {
        return await execution.RunAsync(settings, async () =>
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
                $"Uninstalled {installed.SourceId} ({installed.PackageId})." +
                (installed.RepositoryPath is null ? string.Empty : $" Cached sources retained at {installed.RepositoryPath}."));
            return ExitCodes.Success;
        });
    }

    private static string StyleName(ToolCommandStyle commandStyle)
        => commandStyle == ToolCommandStyle.Dotnet ? "dotnet" : "standalone";

    private static string StyleDescription(ToolCommandStyle commandStyle)
        => commandStyle == ToolCommandStyle.Dotnet ? "a 'dotnet <command>' invocation" : "an unprefixed command";
}
