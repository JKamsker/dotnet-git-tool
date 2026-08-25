using DotnetGitTool.Commands;
using DotnetGitTool.Discovery;
using DotnetGitTool.Infrastructure;
using DotnetGitTool.Output;
using DotnetGitTool.Processes;
using DotnetGitTool.Source;
using DotnetGitTool.State;
using DotnetGitTool.Workflows;
using Spectre.Console.Cli;

var services = new ServiceRegistry();
services.AddSingleton<ICliOutput>(new CliOutput(Console.Out, Console.Error));
services.AddSingleton<IProcessRunner, ProcessRunner>();
services.AddSingleton<DotnetProjectRunner>();
services.AddSingleton<SourceSpecParser>();
services.AddSingleton(new RepositoryCachePath());
services.AddSingleton<RepositoryCache>();
services.AddSingleton<RepositoryCachePruner>();
services.AddSingleton<ProjectDiscovery>();
services.AddSingleton(new InstallationStorePath());
services.AddSingleton<InstallationStore>();
services.AddSingleton<ToolPackager>();
services.AddSingleton<ToolWorkflow>();

var app = new CommandApp(new TypeRegistrar(services));
app.Configure(config =>
{
    config.SetApplicationName("dotnet git-tool");
    config.SetApplicationVersion(
        typeof(SourceSpecParser).Assembly.GetName().Version?.ToString(3) ?? "unknown");
    config.SetExceptionHandler((exception, _) =>
    {
        Console.Error.WriteLine($"error: {exception.Message}");
        return exception is CliException cliException ? cliException.ExitCode : ExitCodes.GeneralError;
    });

    config.AddCommand<InstallCommand>("install")
        .WithDescription("Clone, discover, pack, and globally install a tool from source.")
        .WithExample("install", "JKamsker/bookmeta-cli", "--dry-run")
        .WithExample("install", "JKamsker/bookmeta-cli", "--yes")
        .WithExample("install", "JKamsker/bookmeta-cli", "--yes", "--standalone");
    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Rebuild and update a previously installed source tool.")
        .WithExample("update", "JKamsker/bookmeta-cli", "--dry-run");
    config.AddCommand<UninstallCommand>("uninstall")
        .WithDescription("Uninstall a source tool and remove its recorded state.")
        .WithExample("uninstall", "JKamsker/bookmeta-cli", "--dry-run");
    config.AddCommand<ListCommand>("list")
        .WithDescription("List source tools managed by dotnet git-tool.");
    config.AddBranch("cache", cache =>
    {
        cache.SetDescription("Inspect and maintain retained source repositories.");
        cache.AddCommand<CachePruneCommand>("prune")
            .WithDescription("Remove cached repositories not used by managed installations.")
            .WithExample("cache", "prune", "--dry-run")
            .WithExample("cache", "prune", "--yes");
    });
});

var exitCode = await app.RunAsync(args);
return exitCode == -1 ? ExitCodes.Usage : exitCode;
