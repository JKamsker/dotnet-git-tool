using System.Text.Json;
using DotnetGitTool.Commands;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.State;

namespace DotnetGitTool.Output;

public sealed class CliOutput(TextWriter stdout, TextWriter stderr) : ICliOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public void Status(GlobalSettings settings, string message)
    {
        if (!settings.Json && !settings.Quiet)
        {
            stderr.WriteLine(message);
        }
    }

    public void Diagnostic(GlobalSettings settings, string message)
    {
        if (settings.Verbose && !settings.Json && !settings.Quiet)
        {
            stderr.WriteLine(message);
        }
    }

    public void QueryResult(GlobalSettings settings, object data, string humanMessage)
    {
        if (settings.Json)
        {
            WriteEnvelope(new { ok = true, data, error = (object?)null, meta = Meta() });
        }
        else
        {
            stdout.WriteLine(humanMessage);
        }
    }

    public void Success(GlobalSettings settings, object data, string humanMessage)
    {
        if (settings.Json)
        {
            WriteEnvelope(new { ok = true, data, error = (object?)null, meta = Meta() });
        }
        else if (!settings.Quiet || settings is MutationSettings { DryRun: true })
        {
            stdout.WriteLine(humanMessage);
        }
    }

    public void Failure(GlobalSettings settings, CliException exception)
    {
        if (settings.Json)
        {
            WriteEnvelope(new
            {
                ok = false,
                data = (object?)null,
                error = new { kind = exception.Kind, message = exception.Message },
                meta = Meta(),
            });
        }
        else
        {
            stderr.WriteLine($"error: {exception.Message}");
        }
    }

    public void List(GlobalSettings settings, IReadOnlyList<InstallationRecord> installations)
    {
        if (settings.Json)
        {
            WriteEnvelope(new
            {
                ok = true,
                data = new { installations },
                error = (object?)null,
                meta = Meta(),
            });
            return;
        }

        if (installations.Count == 0)
        {
            stdout.WriteLine("No source tools are installed.");
            return;
        }

        const string format = "{0,-30} {1,-34} {2,-14} {3,-24} {4}";
        stdout.WriteLine(format, "SOURCE", "PACKAGE", "COMMIT", "COMMAND", "CACHE PATH");
        foreach (var item in installations.OrderBy(item => item.SourceId, StringComparer.OrdinalIgnoreCase))
        {
            stdout.WriteLine(format, Truncate(item.SourceId, 30), Truncate(item.PackageId, 34),
                Truncate(item.Commit, 14), item.Command ?? "-", item.RepositoryPath ?? "-");
        }
    }

    private void WriteEnvelope(object envelope) => stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));

    private static object Meta() => new { schemaVersion = 1, warnings = Array.Empty<string>() };

    private static string Truncate(string value, int length)
        => value.Length <= length ? value : string.Concat(value.AsSpan(0, length - 1), "…");
}
