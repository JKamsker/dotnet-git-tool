# Architecture

This page maps the source of `dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`) for contributors and for anyone who wants to know how the parts fit together. It describes code, not behavior: user-facing semantics live in [How it works](how-it-works.md) and the [CLI reference](cli-reference.md).

If you are here to make a change, [Invariants](#invariants) is the checklist to review it against and [Adding a command](#adding-a-command) is the file-by-file recipe. The sections before them are the map you need to follow either one.

## Design summary

`dotnet-git-tool` is a single `net10.0` console project with one NuGet dependency, `Spectre.Console.Cli` 0.55.0. Everything it does is a sequence of calls to two external commands, `git` and `dotnet`, wrapped in a thin orchestration layer: a command parses arguments into a settings object, a workflow drives the install pipeline, and one interface, or seam, is the single interception point where a result becomes human output or the JSON envelope.

`dotnet-git-tool` has two seams, `ICliOutput` and `IProcessRunner`, and both exist so tests can substitute the real console and the real `git` and `dotnet` processes. There is no domain model beyond a handful of records, no database, and no background work.

Four constraints shape the code, and each one is enforced somewhere concrete:

- **No libgit2 and no MSBuild API.** Git work goes through the `git` executable, and project evaluation goes through `dotnet msbuild -getProperty:`. This keeps the dependency set at one package and makes the tool behave exactly like the toolchain you already have installed. The cost is that every failure arrives as an exit code plus captured text, which is why `ProcessResultExtensions.EnsureSuccess` exists. The one exception is `ProjectVersionReader`, which reads a version out of `.csproj` and ancestor `Directory.Build.props` files as plain XML, because `cache list` must report a version for a repository it is not about to build.
- **Every mutation is previewable.** `install`, `update`, `uninstall`, and `cache prune` each have a `settings.DryRun` branch that returns before any network call, any build, and any change to the disk or the global tools directory. The branch runs after local state has been read, so a preview still fails with `already_installed` or `installation_not_found` when the installation record does not match, and `cache prune --dry-run` still enumerates the cache root to build its plan.
- **The repository cache is reset on every use.** `CleanAsync` runs whenever an existing cached repository is reused, again after a successful pack, and again from `CachedRepository.DisposeAsync`. Because the handle is held with `await using`, the failure path cleans up too. A fresh clone skips the first of those three, since a new clone is clean by construction, and a clean that cannot finish throws `repository_cache_dirty` instead of building on a dirty tree.
- **Consent is a code path, not a convention.** Every prompt goes through `InteractionGuard`, which refuses instead of prompting under `--json`, under `--quiet`, or when stdin or stderr is redirected.

## Repository layout

```text
.
├── .github/workflows/         CI: build, test, pack, upload, and the trusted-publishing job.
├── docs/                      This documentation set.
├── eng/                       Resolve-PackageVersion.ps1, the release version script.
├── src/DotnetGitTool/         The tool. The only project that ships.
├── tests/DotnetGitTool.Tests/ The whole test suite.
├── tests/Fixtures/SimpleTool/ A plain console app used as evaluation-only test data.
├── CONTRIBUTING.md            The local build, test, and release loop.
├── Directory.Build.props      Shared MSBuild properties for every project in the repository.
├── dotnet-git-tool.slnx       Solution file listing exactly two projects.
├── global.json                Sets the minimum SDK version (10.0.300) and rolls forward to the newest installed feature band.
├── LICENSE                    The MIT license this repository ships under.
└── README.md                  Packed into the NuGet package as the package readme.
```

[Contributing](../CONTRIBUTING.md) covers the shared `Directory.Build.props` properties. The one that changes how you work is `TreatWarningsAsErrors`, so a compiler or analyzer warning fails the build.

## Source map

Every folder under `src/DotnetGitTool` is one namespace under the `DotnetGitTool` root namespace. The assembly name is `dotnet-git-tool`.

| Location | Key types | Responsibility |
|---|---|---|
| `Program.cs` | top-level statements | Composition root: builds `ServiceRegistry`, configures `CommandApp`, registers the exception handler, returns the exit code. |
| `DotnetGitTool.Commands` | `GlobalSettings`, `MutationSettings`, `ToolCommandSettings`, `InstallCommand`, `UpdateCommand`, `UninstallCommand`, `ListCommand`, `CacheListCommand`, `CacheShowCommand`, `CachePruneCommand` | The command-line surface. One settings class plus one command class per verb. `install`, `update`, and `uninstall` delegate their bodies to `ToolWorkflow`; every other command holds its own body. |
| `DotnetGitTool.Workflows` | `ToolWorkflow`, `InteractionGuard`, `ToolPackager`, `PackedTool`, `ToolPackageIdentity`, `ToolCommandIdentity`, `ToolCommandStyle` | Orchestration. `ToolWorkflow` runs install, update, and uninstall end to end. `ToolPackager` runs `dotnet pack` into a temp directory and hands back a `PackedTool` that deletes it on dispose. The identity types derive the generated package ID, the generated version, and the packaged command name. |
| `DotnetGitTool.Discovery` | `ProjectDiscovery`, `ProjectMetadata`, `ProjectSelection`, `RepositoryManifest` | Project discovery and project evaluation: enumerate `*.csproj`, read the repository manifest, run MSBuild for four properties, rank the candidates, resolve the command name. |
| `DotnetGitTool.Source` | `SourceSpec`, `SourceSpecParser`, `RepositoryCache`, `RepositoryCachePath`, `CachedRepository`, `RepositoryCacheInspector`, `RepositoryCachePruner`, `CachedRepositoryInfo`, `ProjectVersionReader` | The repository argument and everything on disk under the cache root: parsing and validating a source ID, cloning and refreshing a cached repository, locking, and cleaning. `RepositoryCacheInspector` and `RepositoryCachePruner` are the bodies of the `cache` verbs, and `ProjectVersionReader` reads a version out of the checked-out project without invoking MSBuild. |
| `DotnetGitTool.State` | `InstallationStore`, `InstallationStorePath`, `InstallationRecord`, `InstallationState` | The installation state file. Resolves its path, reads and validates `schemaVersion`, and writes atomically under a lock. |
| `DotnetGitTool.Output` | `ICliOutput`, `CliOutput`, `CacheRepositoryFormatter`, `CacheRepositoryTableRenderer` | Owns all result and status output: the JSON envelope, the `list` columns, the `cache show` label block, and the `cache list` table. The only two writers outside this namespace are the confirmation prompt in `InteractionGuard` and the fallback `error:` line in `Program.cs`. |
| `DotnetGitTool.Processes` | `IProcessRunner`, `ProcessRunner`, `ProcessResult`, `ProcessResultExtensions`, `DotnetProjectRunner` | The only place that starts an external command. Owns argument passing, output capture, failure translation, and the SDK fallback. |
| `DotnetGitTool.Infrastructure` | `ExitCodes`, `CliException`, `TypeRegistrar`, `ServiceRegistry` | Cross-cutting primitives: the exit-code constants, the exception type that carries an error kind and an exit code, and the container that Spectre.Console.Cli resolves through. |

`ProjectVersionReader` is the one type that parses an MSBuild file by hand. It creates its `XmlReader` with `DtdProcessing.Prohibit` and `XmlResolver = null`, returns `null` for a project file or an ancestor directory that is a reparse point, and skips any value containing `$(`, so an unexpanded property never becomes a version string.

## Call chain

`ToolWorkflow.InstallAsync` is the longest path through the program. A real install enters these types in this order:

1. `ToolCommandSettings.ResolveCommandStyleOverride`
2. `SourceSpecParser.Parse`
3. `InstallationStore.FindAsync`
4. `InteractionGuard.ConfirmCodeExecution`
5. `RepositoryCache.PrepareAsync`
6. `ProjectDiscovery.DiscoverAsync`
7. `ToolPackageIdentity.GeneratePackageId` and `ToolPackageIdentity.GenerateVersion`
8. `ToolCommandIdentity.Create`
9. `ToolPackager.PackAsync`
10. `CachedRepository.CleanAsync`
11. `IProcessRunner.RunAsync` with `dotnet tool install --global`
12. `InstallationStore.AddAsync`, then `ICliOutput.Success`

`UpdateAsync` follows the same order, reusing the recorded generated package ID and calling `ReplaceAsync` instead of `AddAsync`. A dry run returns between steps 3 and 4. [How it works](how-it-works.md) explains what each stage does; this list is only the order in which the types are entered.

## Composition root

`Program.cs` is a top-level-statements file with no startup class. It builds a `ServiceRegistry`, registers every service by hand in file order, and passes a `TypeRegistrar` wrapper to `CommandApp`.

`ServiceRegistry` (in `Infrastructure/TypeRegistrar.cs`) is a small hand-written container, and registrations come in two shapes. `AddSingleton<TService, TImplementation>()` and `AddSingleton<TImplementation>()` store a lazy factory: `RegisterLazy` creates the instance once under a lock, adds it to a disposables list if it implements `IDisposable`, and returns the same instance afterwards. `AddSingleton<TService>(TService instance)` takes an object you already built and routes to `RegisterInstance`, which stores `() => instance` with no lock, no lazy creation, and no disposable tracking.

`Create` picks the public constructor with the most parameters and resolves each parameter type through `Get`, which throws `InvalidOperationException` naming the type when nothing is registered for it. `TryGet` additionally answers `IEnumerable<T>` by collecting every registration assignable to `T`. Disposal walks the list in reverse.

`TypeRegistrar` adapts that registry to Spectre's `ITypeRegistrar`. Spectre calls `Register`, `RegisterInstance`, and `RegisterLazy` while the app is being configured, then calls `Build()` to get an `ITypeResolver` before executing. Resolution of a command type therefore runs through the same constructor injection as everything else, which is why `InstallCommand(ToolWorkflow workflow)` works without any attribute or factory.

Four registrations are instances rather than types, because they capture ambient state at startup:

```csharp
services.AddSingleton<ICliOutput>(new CliOutput(Console.Out, Console.Error));
services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
services.AddSingleton(new RepositoryCachePath());
services.AddSingleton(new InstallationStorePath());
```

`RepositoryCachePath` and `InstallationStorePath` resolve `DOTNET_GIT_TOOL_CACHE`, `DOTNET_GIT_TOOL_HOME`, and the XDG variables in their constructors, so each is read once per process. [Configuration](configuration.md#environment-variables) documents both precedence chains.

Configuration then sets the application name to `dotnet git-tool` and the application version to the assembly version rendered with `ToString(3)`, registers the exception handler, and adds four top-level commands plus a `cache` branch holding `list`, `show`, and `prune`. Descriptions and examples are attached with `.WithDescription(...)` and `.WithExample(...)` at registration time.

## Command settings inheritance

Settings classes form a three-layer chain, and each command picks the layer that matches what it is allowed to do. Flag semantics belong to the [CLI reference](cli-reference.md); this table is about which class contributes what.

| Settings class | Flags it contributes | Commands built on it |
|---|---|---|
| `GlobalSettings` | `--json`, `--quiet`, `--verbose`, `--no-color` | every command |
| `MutationSettings : GlobalSettings` | `--dry-run`, `-y, --yes` | `uninstall`, `cache prune`, and both classes below |
| `ToolCommandSettings : MutationSettings` | `--standalone`, `--dotnet-command` | `install`, `update` |

Leaf classes add only their own argument and options. `InstallSettings` and `UpdateSettings` add the `<REPOSITORY>` argument, `--ref <REF>`, and `-p, --project <PATH>`; `UninstallSettings` (on `MutationSettings`) and `CacheShowSettings` (on `GlobalSettings`) add `<REPOSITORY>` alone. All four override `Validate()` to reject a blank repository argument: three fail with the message `A repository is required.` while `CacheShowSettings` fails with `A repository name is required.` instead. `ListSettings`, `CacheListSettings`, and `CachePruneSettings` add nothing at all.

Two behaviors ride on the chain. `ToolCommandSettings.ResolveCommandStyleOverride()` is the single place that turns the two command-style flags into a `ToolCommandStyle?`, and it throws `invalid_command_style` when both are passed. And because every `ICliOutput` method takes a `GlobalSettings`, the output layer cannot see `DryRun` directly; `CliOutput.Success` pattern-matches `settings is MutationSettings { DryRun: true }` to keep a preview visible even under `--quiet`.

> [!NOTE]
> `GlobalSettings.NoColor` is declared and never read anywhere else in the source tree, so `--no-color` currently changes nothing. What `NO_COLOR` still reaches is recorded in [Configuration](configuration.md#environment-variables).

## Error handling

`CliException` lives in `Infrastructure/ExitCodes.cs` next to the exit-code constants:

```csharp
public class CliException(string message, string kind, int exitCode = ExitCodes.GeneralError)
    : Exception(message)
{
    public string Kind { get; } = kind;
    public int ExitCode { get; } = exitCode;
}
```

`CliException` is public while the surrounding `ExitCodes` class is `internal`, so the test project can catch and assert on the exception but writes the numbers as literals. `Kind` becomes `error.kind` in the JSON envelope and `ExitCode` becomes the process exit code; the [error-kind table](cli-reference.md#error-kinds) lists every kind the program can emit.

Almost every type throws it: `SourceSpecParser`, `ProjectDiscovery`, `ToolCommandIdentity`, `RepositoryCache`, `RepositoryCacheInspector`, `RepositoryCachePruner`, `InstallationStore`, `InteractionGuard`, `ToolCommandSettings`, `ToolWorkflow`, `ProcessRunner`, and `ProcessResultExtensions`.

Three layers catch it, in this order.

1. `ToolWorkflow.ExecuteAsync` wraps the body of `InstallAsync`, `UpdateAsync`, and `UninstallAsync` in one try/catch and reports through `ICliOutput.Failure`.
2. Every command that does not route through `ToolWorkflow` repeats that same three-catch block inline. `ListCommand`, `CacheListCommand`, `CacheShowCommand`, and `CachePruneCommand` are identical here. `CachePruneCommand` mutates but keeps its body in the command class, because it never touches the repository cache handle, the packer, or the installation record.
3. Anything thrown before a command body runs reaches the handler registered with `config.SetExceptionHandler`, which writes `error: <MESSAGE>` to stderr and returns `CliException.ExitCode` or `ExitCodes.GeneralError`.

Layers 1 and 2 use an identical mapping:

| Exception | Error kind | Exit code |
|---|---|---|
| `CliException` | its own `Kind` | its own `ExitCode` |
| `OperationCanceledException` | `cancelled` | `10` |
| Any other exception | `unexpected_error` | `1` |

Layer 3 covers Spectre's own argument parsing and every `CommandSettings.Validate()` override, and it never produces the JSON envelope, so `--json` plus a malformed command line yields empty stdout, a plain-text stderr line, and exit `1`. Registering a handler also means Spectre never returns `-1`, which makes the `-1` branch of the `exitCode == -1 ? ExitCodes.Usage : exitCode` expression at the end of `Program.cs` dead code.

## The process layer

`IProcessRunner` is one method and one record:

```csharp
Task<ProcessResult> RunAsync(
    string fileName,
    IEnumerable<string> arguments,
    string? workingDirectory = null,
    CancellationToken cancellationToken = default);
```

`ProcessRunner` fills a `ProcessStartInfo` with `UseShellExecute = false` and pushes each argument into `ArgumentList`, so no shell is involved and callers never quote or escape an argument themselves. Stdout and stderr are redirected and read to the end; stdin is not redirected, so a child process inherits the console. A `Win32Exception` on start becomes a `CliException` with kind `dependency_not_found`, which is what a missing `git` or `dotnet` looks like.

The interface is a seam for tests. `DotnetProjectRunnerTests.RecordingProcessRunner` is a hand-written `IProcessRunner` that records every argument vector and working directory and returns scripted results, which is how the SDK fallback is tested without installing several SDKs.

`ProcessResultExtensions.EnsureSuccess(result, operation)` is the single funnel for external command failures. It throws `CliException` with kind `child_process_failed` and the default exit code `1`, using the message `"<OPERATION> failed: <DETAIL>"`, or `"<OPERATION> failed with exit code <N>."` when the child printed nothing. The detail comes from `FirstUsefulLine`, which despite its name returns `LastOrDefault()` over the non-empty trimmed lines, preferring stderr over stdout. Combined with full buffering, that means one line of a failed build reaches the user and the rest is discarded.

`DotnetProjectRunner` wraps `IProcessRunner` for `dotnet` invocations that run inside a cached repository. It is not itself an `IProcessRunner`: it hard-codes the file name and takes the repository path as a required parameter, so it cannot be registered in place of one.

It runs the command once with the repository as the working directory. Only when the captured output contains `https://aka.ms/dotnet/sdk-not-found` does it read `sdk.version` from the repository's `global.json`, probe `dotnet --list-sdks`, and, if a strictly newer SDK is installed, re-run the identical argument vector from a fresh temp directory that has no `global.json` above it. In every other case, including a marker with no newer SDK available, the original failure is returned unchanged.

`ProjectDiscovery` and `ToolPackager` go through this wrapper; the `dotnet tool install`, `update`, and `uninstall` calls in `ToolWorkflow` use the raw `IProcessRunner`, because they do not run in the clone.

## The output layer

`ICliOutput` is the seam that keeps `Console` out of the rest of the code. It has six methods:

| Method | Stream | Written when |
|---|---|---|
| `Status` | stderr | neither `--json` nor `--quiet` is set |
| `Diagnostic` | stderr | `--verbose` is set and neither `--json` nor `--quiet` is |
| `QueryResult` | stdout | always: the envelope under `--json`, the human form otherwise |
| `Success` | stdout | always under `--json`; otherwise unless `--quiet`, except that a `MutationSettings` preview still prints |
| `Failure` | stdout under `--json`, stderr otherwise | always |
| `List` | stdout | always |

The split to remember is that results go to stdout and everything conversational goes to stderr, and [Streams](cli-reference.md#streams) has the full matrix. Progress lines, verbose diagnostics, confirmation prompts, and the `error:` line are stderr; the table, the label block, the success line, the preview, and the envelope are stdout. `QueryResult`, `List`, and `CacheRepositoryTableRenderer` never consult `settings.Quiet`, so `list`, `cache list`, and `cache show` print their payload under `--quiet`.

`CliOutput` is constructed with two `TextWriter` values rather than reaching for `Console` directly, so tests pass `StringWriter` and assert on the exact bytes. `WriteEnvelope` serializes an anonymous `{ ok, data, error, meta }` object with `JsonSerializerDefaults.Web` and `WriteIndented = true`, and `Meta()` returns `schemaVersion = 1` with an empty `warnings` array. `List` writes fixed-width columns with the format string `{0,-30} {1,-34} {2,-14} {3,-24} {4}`.

Two formatters sit beside `CliOutput`. `CacheRepositoryFormatter.Show` builds the `cache show` block as label-and-value lines with the label padded to 15 characters and dates rendered in UTC. `CacheRepositoryTableRenderer` is the only surface this program renders through Spectre.Console: it takes `IAnsiConsole`, builds a rounded, expanded four-column `Table`, escapes the source and version cells with `Markup.Escape`, and formats dates as `dd.MM.yyyy` under `CultureInfo.InvariantCulture` after converting to local time.

The two date cells are passed unescaped, because they come from a fixed format string. Spectre.Console.Cli renders the help and version screens on its own, which is why those screens come out in the machine's display language.

## Tests

`tests/DotnetGitTool.Tests` holds 43 tests in 12 classes. Two seams make that possible without a mocking library: `IProcessRunner` lets a test script `git` and `dotnet` results, and `ICliOutput` takes `TextWriter` values so a test can assert on exact bytes. Framework versions and the conventions for writing a new test live in [Contributing](../CONTRIBUTING.md).

The suite is a pyramid:

- Pure unit tests over deterministic logic: `SourceSpecParserTests`, `PackageIdentityTests`, `ToolCommandIdentityTests`, `ProjectDiscoveryTests` (the ranking half), `CliOutputTests`, `CacheRepositoryTableRendererTests`, `InstallationStoreTests`, `DotnetProjectRunnerTests`.
- Filesystem tests: `RepositoryCacheTests` builds a Git repository in a temp directory and clones from it as a local origin; `RepositoryCacheInspectorTests` runs `git init` inside the cache directory itself and sets an `origin` URL it never contacts; `RepositoryCachePrunerTests` creates plain directories and never invokes `git`. All three clear read-only attributes before deleting, because `.git` object files are read-only.
- End-to-end tests that spawn the built `dotnet-git-tool` assembly as a child process: `CliProcessTests`. It locates the assembly with `typeof(SourceSpecParser).Assembly.Location`, runs it through the `dotnet` driver, and asserts exit codes, envelope contents, and that stderr is byte-empty under `--json`.

`tests/Fixtures/SimpleTool` is a plain console app with `OutputType=Exe` and `AssemblyName=fixture-command`, no `PackAsTool`, no `ToolCommandName`, and no repository manifest. It is deliberately absent from `dotnet-git-tool.slnx`, and it sits outside the test project directory, so the default `**/*.cs` glob never treats it as a compilation candidate. Only `ProjectDiscoveryTests.EvaluatesAnOrdinaryConsoleProjectWithMsBuild` touches it, through `dotnet msbuild -getProperty:`, which evaluates without restoring. That test finds the fixture with the hard-coded relative path `AppContext.BaseDirectory/../../../../../tests/Fixtures/SimpleTool`, so changing `BaseOutputPath` or adding a runtime identifier subfolder breaks it.

In-process tests isolate state by constructing `RepositoryCachePath` and `InstallationStorePath` with temp paths, which is why both types take an optional path in their constructor. A test that spawns the assembly cannot inject anything, so it sets `DOTNET_GIT_TOOL_CACHE` and `DOTNET_GIT_TOOL_HOME` instead, as `CliProcessTests.CachePruneEnvironment` does.

## Invariants

Check a change against this list before opening a pull request.

| Invariant | Enforced by |
|---|---|
| The repository cache is left clean: hold a `CachedRepository` with `await using`, call `CleanAsync` after anything that builds, and never write your own files into a cache directory. | `CachedRepository.CleanAsync`, `CachedRepository.DisposeAsync` |
| A `--dry-run` branch returns before `InteractionGuard`, before `RepositoryCache.PrepareAsync`, and before any `dotnet` invocation. | the `settings.DryRun` branch in each mutating verb |
| Exit codes come only from `ExitCodes`. Do not introduce a new numeric literal. | `Infrastructure/ExitCodes.cs` |
| The JSON envelope keeps its four top-level keys `ok`, `data`, `error`, and `meta`, and `meta.schemaVersion` stays at `1`. Adding a key inside `data` is additive; changing the envelope is not. | `CliOutput.WriteEnvelope`, `CliOutput.Meta` |
| Nothing prompts when `settings.Json`, `settings.Quiet`, `Console.IsInputRedirected`, or `Console.IsErrorRedirected` is set. | `InteractionGuard` |
| Nothing outside the cache root and the tool's own temp directories is deleted. | `RepositoryCachePruner.EnsureDirectChild`, which must run for every candidate path |
| An external command whose failure should abort the operation goes through `EnsureSuccess`, so it surfaces as `child_process_failed` rather than an unhandled exception. | `ProcessResultExtensions.EnsureSuccess` |
| A ref that arrives from the command line reaches `git` only after validation. | `SourceSpecParser.Parse` |
| Every command body catches `CliException`, `OperationCanceledException`, and `Exception`, and reports through `ICliOutput.Failure`. | `ToolWorkflow.ExecuteAsync` and the inline block in every other command |
| Nothing writes to `Console` outside `Output/`, `Workflows/InteractionGuard.cs`, and the `Program.cs` exception handler. | code review |
| The build is warning-free. | `TreatWarningsAsErrors` in `Directory.Build.props` |

Four of those rules have exceptions the current code relies on, so read them before you extend the rule to new code.

- A dry run reads local state before it returns. `install --dry-run` on a managed source still fails with `already_installed`, `update --dry-run` and `uninstall --dry-run` on an unmanaged source still fail with `installation_not_found`, and `cache prune --dry-run` still enumerates the cache root to build its plan.
- The tool's own temp directories are deleted on every run: the pack output under `<TEMP>/dotnet-git-tool-package-<GUID>` and the SDK-fallback working directory under `<TEMP>/dotnet-git-tool-sdk-<SUFFIX>`.
- Three callers deliberately skip `EnsureSuccess`. `RepositoryCacheInspector.GitValueAsync` turns a failed probe into `null`, which is how `isGitRepository: false`, `branch: null`, and `isDirty: null` are produced; the `--list-sdks` probe in `DotnetProjectRunner` treats a failure as "no newer SDK"; and the rollback `dotnet tool uninstall` in `ToolWorkflow` runs unchecked so it cannot mask the original error.
- A ref replayed from `installed.json` on `update` is not revalidated, so any new path that reconstructs a `SourceSpec` from an installation record must validate it. The leading-dash rejection is an argument-injection guard, not cosmetic validation.

The exit codes in use are `0`, `1`, `2`, `5`, `6`, and `10`; `3`, `4`, `7`, `8`, and `9` are never produced, so scripts must not treat the range as dense. A new constant belongs in the [exit-code table](cli-reference.md#exit-codes) in the same change, and any envelope change belongs in [the JSON envelope](cli-reference.md#the-json-envelope).

## Adding a command

Adding `dotnet git-tool <VERB>` touches these files, in this order.

1. `src/DotnetGitTool/Commands/<Verb>Command.cs`. Add a settings class deriving from `GlobalSettings` for a read-only verb, `MutationSettings` for one that changes something, or `ToolCommandSettings` for one that also selects a command style. Declare arguments with `[CommandArgument]` and options with `[CommandOption]`, give each a `[Description]`, and override `Validate()` for shape checks. A failed `Validate()` exits `1` through the top-level handler, with no envelope.
2. The same file. Add the command class deriving from `AsyncCommand<TSettings>` and take its dependencies as constructor parameters. The project has no `InternalsVisibleTo`, so a type the test project constructs or asserts on has to be `public`; keep everything else `internal`, the way `InteractionGuard`, `ServiceRegistry`, and `ProcessResultExtensions` are.
3. `src/DotnetGitTool/Workflows/ToolWorkflow.cs`, for a verb that clones, builds, packs, or writes an installation record. Put the body there and wrap it in `ExecuteAsync` so it inherits the three-catch block. Every other verb keeps its body in the command class and repeats the catch block, the way `ListCommand` does and the way `CachePruneCommand` does even though it mutates.
4. `src/DotnetGitTool/Program.cs`. Register any new dependency with `services.AddSingleton` before the `CommandApp` is constructed, then add `config.AddCommand<TCommand>("<verb>")` with a description, or add it inside the `cache` branch. Attach an example with `.WithExample(...)` for any verb that takes an argument; `list` takes none and has none.
5. Emit results only through `ICliOutput`: `QueryResult` for a read-only verb, `Success` for a mutation, `Failure` for every error. Add a new formatter under `Output/` rather than building strings in the command.
6. `tests/DotnetGitTool.Tests`. Cover pure logic in a focused class, and add an end-to-end case to `CliProcessTests` with `DOTNET_GIT_TOOL_CACHE` and `DOTNET_GIT_TOOL_HOME` pointed at temp directories. The existing help tests assert on flag names, so renaming a flag breaks a test by design.
7. `docs/cli-reference.md`. The command is not done until its options, exit codes, and `data` keys are documented there.

## See also

- [Documentation index](README.md)
- [How it works](how-it-works.md)
- [CLI reference](cli-reference.md)
- [Repository cache](repository-cache.md)
- [Contributing](../CONTRIBUTING.md)
