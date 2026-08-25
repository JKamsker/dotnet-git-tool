using System.Globalization;
using DotnetGitTool.Source;

namespace DotnetGitTool.Output;

public static class CacheRepositoryFormatter
{
    public static string Show(CachedRepositoryInfo repository)
    {
        var details = new (string Label, string Value)[]
        {
            ("Source", repository.SourceId),
            ("Managed", repository.IsManaged ? "yes" : "no"),
            ("Git repository", repository.IsGitRepository ? "yes" : "no"),
            ("Path", repository.Path),
            ("Origin", repository.Origin ?? "-"),
            ("Branch", repository.Branch ?? (repository.IsGitRepository ? "detached" : "-")),
            ("Commit", repository.Commit ?? "-"),
            ("Revision", repository.Revision ?? "-"),
            ("Commit date", FormatDate(repository.CommitDate)),
            ("Working tree", repository.IsDirty switch { true => "dirty", false => "clean", null => "unknown" }),
            ("Size", FormatSize(repository.SizeBytes)),
            ("Installed", FormatDate(repository.Installation?.InstalledAt)),
            ("Updated", FormatDate(repository.Installation?.UpdatedAt)),
            ("Package ID", repository.Installation?.PackageId ?? "-"),
            ("Package version", repository.Installation?.Version ?? "-"),
            ("Command", repository.Installation?.Command ?? "-"),
            ("Project", repository.Installation?.Project ?? "-"),
            ("Requested ref", repository.Installation?.RequestedRef ?? "-"),
        };
        return string.Join(Environment.NewLine, details.Select(detail => $"{detail.Label,-15} {detail.Value}"));
    }

    private static string FormatDate(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatSize(long? value)
    {
        if (value is null)
        {
            return "unknown";
        }

        const double bytesPerKilobyte = 1024;
        const double bytesPerMegabyte = bytesPerKilobyte * 1024;
        const double bytesPerGigabyte = bytesPerMegabyte * 1024;
        return value.Value switch
        {
            >= (long)bytesPerGigabyte => FormattableString.Invariant(
                $"{value.Value / bytesPerGigabyte:F2} GiB ({value.Value} bytes)"),
            >= (long)bytesPerMegabyte => FormattableString.Invariant(
                $"{value.Value / bytesPerMegabyte:F2} MiB ({value.Value} bytes)"),
            >= (long)bytesPerKilobyte => FormattableString.Invariant(
                $"{value.Value / bytesPerKilobyte:F2} KiB ({value.Value} bytes)"),
            _ => $"{value.Value} bytes",
        };
    }
}
