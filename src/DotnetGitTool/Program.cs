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
services.AddSingleton<SourceSpecParser>();
services.AddSingleton<RepositoryCloner>();
services.AddSingleton<ProjectDiscovery>();
services.AddSingleton<InstallationStore>();
services.AddSingleton<ToolWorkflow>();

var app = new CommandApp(new TypeRegistrar(services));
app.Configure(config =>
{
    config.SetApplicationName("dotnet git-tool");
    config.SetApplicationVersion("0.1.0");
    config.SetExceptionHandler((exception, _) =>
    {
        Console.Error.WriteLine($"error: {exception.Message}");
        return exception is CliException cliException ? cliException.ExitCode : ExitCodes.GeneralError;
    });

    config.AddCommand<InstallCommand>("install")
        .WithDescription("Clone, discover, pack, and globally install a tool from source.")
        .WithExample("install", "JKamsker/bookmeta-cli", "--dry-run")
        .WithExample("install", "JKamsker/bookmeta-cli", "--yes");
    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Rebuild and update a previously installed source tool.")
        .WithExample("update", "JKamsker/bookmeta-cli", "--dry-run");
    config.AddCommand<UninstallCommand>("uninstall")
        .WithDescription("Uninstall a source tool and remove its recorded state.")
        .WithExample("uninstall", "JKamsker/bookmeta-cli", "--dry-run");
    config.AddCommand<ListCommand>("list")
        .WithDescription("List source tools managed by dotnet git-tool.");
});

return await app.RunAsync(args);
