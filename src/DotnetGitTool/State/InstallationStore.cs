using System.Text.Json;
using DotnetGitTool.Infrastructure;

namespace DotnetGitTool.State;

public sealed class InstallationStore(InstallationStorePath storePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string StatePath => Path.Combine(storePath.Value, "installed.json");

    public async Task<IReadOnlyList<InstallationRecord>> ListAsync(CancellationToken cancellationToken = default)
        => (await ReadAsync(cancellationToken)).Installations;

    public async Task<InstallationRecord?> FindAsync(string sourceId, CancellationToken cancellationToken = default)
        => (await ReadAsync(cancellationToken)).Installations.FirstOrDefault(
            item => item.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase));

    public async Task AddAsync(InstallationRecord record, CancellationToken cancellationToken = default)
    {
        await using var stateLock = await AcquireLockAsync(cancellationToken);
        var state = await ReadAsync(cancellationToken);
        if (state.Installations.Any(item => item.SourceId.Equals(record.SourceId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CliException(
                $"'{record.SourceId}' is already managed. Use 'dotnet git-tool update {record.SourceId}'.",
                "already_installed",
                ExitCodes.Conflict);
        }

        state.Installations.Add(record);
        await WriteAsync(state, cancellationToken);
    }

    public async Task ReplaceAsync(InstallationRecord record, CancellationToken cancellationToken = default)
    {
        await using var stateLock = await AcquireLockAsync(cancellationToken);
        var state = await ReadAsync(cancellationToken);
        state.Installations.RemoveAll(item => item.SourceId.Equals(record.SourceId, StringComparison.OrdinalIgnoreCase));
        state.Installations.Add(record);
        await WriteAsync(state, cancellationToken);
    }

    public async Task RemoveAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        await using var stateLock = await AcquireLockAsync(cancellationToken);
        var state = await ReadAsync(cancellationToken);
        state.Installations.RemoveAll(item => item.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
        await WriteAsync(state, cancellationToken);
    }

    private async Task<InstallationState> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StatePath))
        {
            return InstallationState.Empty;
        }

        try
        {
            await using var stream = File.OpenRead(StatePath);
            var state = await JsonSerializer.DeserializeAsync<InstallationState>(stream, JsonOptions, cancellationToken);
            if (state is null || state.SchemaVersion != 1)
            {
                throw new JsonException("Unsupported or missing schema version.");
            }

            return state;
        }
        catch (JsonException exception)
        {
            throw new CliException($"Could not read state file '{StatePath}': {exception.Message}", "invalid_state");
        }
    }

    private async Task WriteAsync(InstallationState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(StatePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"installed-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, StatePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(StatePath)!;
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, "installed.lock");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (IOException)
            {
                throw new CliException("Timed out waiting for another dotnet git-tool operation to finish.",
                    "state_locked",
                    ExitCodes.Conflict);
            }
        }
    }
}

public sealed class InstallationStorePath
{
    public InstallationStorePath(string? path = null) => Value = path is null ? Resolve() : Path.GetFullPath(path);

    public string Value { get; }

    private static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_GIT_TOOL_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgData))
        {
            return Path.Combine(xdgData, "dotnet-git-tool");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dotnet-git-tool");
    }
}
