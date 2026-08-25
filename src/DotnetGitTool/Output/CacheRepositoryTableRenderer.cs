using System.Globalization;
using DotnetGitTool.Source;
using Spectre.Console;

namespace DotnetGitTool.Output;

public sealed class CacheRepositoryTableRenderer(IAnsiConsole console)
{
    public void Render(RepositoryCacheInventory inventory)
    {
        if (inventory.Repositories.Count == 0)
        {
            console.WriteLine("No cached repositories found.");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand();
        table.AddColumn(new TableColumn("Source"));
        table.AddColumn(new TableColumn("Version").NoWrap());
        table.AddColumn(new TableColumn("Installed at").NoWrap());
        table.AddColumn(new TableColumn("Published at").NoWrap());

        foreach (var repository in inventory.Repositories)
        {
            table.AddRow(
                Markup.Escape(repository.SourceId),
                Markup.Escape(Version(repository)),
                Date(repository.Installation?.InstalledAt),
                Date(repository.CommitDate));
        }

        console.Write(table);
    }

    private static string Version(CachedRepositoryInfo repository)
    {
        var version = repository.SourceVersion ?? repository.Installation?.Version ?? "-";
        var commit = repository.Commit is null
            ? "-"
            : repository.Commit[..Math.Min(12, repository.Commit.Length)];
        return $"[{version}|{commit}]";
    }

    private static string Date(DateTimeOffset? value)
        => value?.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-";
}
