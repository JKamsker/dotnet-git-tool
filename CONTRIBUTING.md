# Contributing

Thanks for working on `dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`).
This page covers the local loop: prerequisites, build, test, run, and the conventions a pull request has to meet.

For the source map, the Spectre.Console.Cli wiring, and how to add a command, read the [architecture guide](docs/architecture.md). For end-user behavior, start at the [documentation index](docs/README.md).

## Prerequisites

- A .NET SDK that satisfies [global.json](global.json), which pins `10.0.300` with `rollForward: latestFeature`. Run `dotnet --list-sdks` to see what you have. The third digit group is the feature band, so `10.0.300` and `10.0.400` satisfy the pin while `10.0.100` and `10.0.200` do not. Install a matching SDK from the [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0) if none is listed.
- `git` on `PATH`. `dotnet-git-tool` runs `git` as an external command for every network operation, and several tests drive a real `git` binary.
- `dotnet` on `PATH`. `ProjectDiscoveryTests` evaluates the fixture with `dotnet msbuild -getProperty:`, and `CliProcessTests` spawns the built tool through the `dotnet` driver, so the SDK above has to be reachable by name.
- PowerShell (`pwsh`) if you want to run `eng/Resolve-PackageVersion.ps1` locally. Nothing else in the repository needs it.

Fork the repository on GitHub, then clone your fork, substituting your account name:

```console
git clone https://github.com/<GITHUB_USER>/dotnet-git-tool.git
```

Maintainers with write access clone `https://github.com/JKamsker/dotnet-git-tool.git` instead and skip the fork.

> [!NOTE]
> The clone above is enough. Do not use `--depth`, `--no-tags`, or a mirror that drops tags: `eng/Resolve-PackageVersion.ps1` throws without the `nuget-v0.0.0` tag, which is also why CI checks out with `fetch-depth: 0`.

## Build and test

The three build-and-test commands CI runs, in the same order:

```console
dotnet restore dotnet-git-tool.slnx
dotnet build dotnet-git-tool.slnx --configuration Release --no-restore
dotnet test dotnet-git-tool.slnx --configuration Release --no-build --no-restore
```

Day to day, one command covers all three:

```console
dotnet test dotnet-git-tool.slnx --configuration Release
```

Run a single class or a single test with a VSTest filter:

```console
dotnet test dotnet-git-tool.slnx --configuration Release --filter "FullyQualifiedName~SourceSpecParserTests"
```

`dotnet-git-tool.slnx` contains exactly two projects, the tool and the test project. `tests/Fixtures/SimpleTool` stays out of the solution on purpose: `ProjectDiscoveryTests` only evaluates it with `dotnet msbuild -getProperty:`, so it is never restored or compiled.

## Isolate your development state

`dotnet-git-tool` writes to the repository cache and to the installation state file (`installed.json`) under your home directory. Point both at throwaway directories before you run a local build, so a debugging session leaves your real repository cache and your real installation records untouched.

Linux and macOS:

```bash
export DOTNET_GIT_TOOL_CACHE=/home/you/scratch/dotnet-git-tool/cache
export DOTNET_GIT_TOOL_HOME=/home/you/scratch/dotnet-git-tool/state
```

Windows (PowerShell):

```powershell
$env:DOTNET_GIT_TOOL_CACHE = "C:\Users\You\scratch\dotnet-git-tool\cache"
$env:DOTNET_GIT_TOOL_HOME = "C:\Users\You\scratch\dotnet-git-tool\state"
```

Both variables name a directory. Use absolute paths: a relative value is resolved against the current working directory, so it silently changes meaning depending on where you run the command. [Configuration](docs/configuration.md) defines both in full.

Neither variable isolates the .NET global tool set. A real `install` from a locally built tool runs `dotnet tool install --global` for real, and with a throwaway state directory the resulting global tool has no installation record, so `dotnet git-tool list` will not show it and `dotnet git-tool uninstall` cannot remove it. Pair the two variables with `--dry-run` when you want a run that installs nothing, and remove anything you did install with `dotnet tool uninstall --global <PACKAGE_ID>`.

> [!WARNING]
> Without these two variables, a locally built `install` or `update` writes to your real repository cache and your real installation records, `uninstall` rewrites your real installation records, and `cache prune` deletes from your real repository cache.

## Run your build

Run the tool straight from the source tree. Everything after `--` goes to the tool:

```console
dotnet run --project src/DotnetGitTool -- --version
dotnet run --project src/DotnetGitTool -- install JKamsker/bookmeta-cli --dry-run
```

To check the packaged command end to end, pack the project and install it as a .NET global tool. `dotnet tool install` fails when a copy is already installed, so uninstall first; the uninstall line reports an error if nothing is installed yet, which is harmless. Pin the version on both halves: `--add-source` adds a NuGet feed next to nuget.org instead of replacing it, so an unpinned install can resolve the published `JKToolKit.Git.Tool` rather than your local pack.

```console
dotnet tool uninstall --global JKToolKit.Git.Tool
dotnet pack src/DotnetGitTool --configuration Release --output artifacts -p:Version=0.0.1-local
dotnet tool install --global JKToolKit.Git.Tool --version 0.0.1-local --add-source artifacts
dotnet tool list --global
```

`artifacts/` is ignored by Git. `dotnet tool list --global` is the check that the local pack won: it lists `JKToolKit.Git.Tool` with the version you installed, `0.0.1-local`. `dotnet git-tool --version` prints `0.0.1` either way, because the assembly version drops the prerelease suffix.

Remove the local build when you are done:

```console
dotnet tool uninstall --global JKToolKit.Git.Tool
```

## Code style

[Directory.Build.props](Directory.Build.props) applies to every project in the repository:

```xml
<PropertyGroup>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <AnalysisLevel>latest</AnalysisLevel>
</PropertyGroup>
```

In practice, a warning fails the build, locally and in CI. That includes nullable warnings and analyzer diagnostics from the latest analysis level. Fix the cause rather than suppressing the diagnostic; when a suppression is the right answer, keep it local and explain it.

The repository has no `.editorconfig` and no formatting step in CI, so match the surrounding code. The existing style is consistent:

- File-scoped namespaces.
- `sealed` on classes and records unless something derives from them. `CliException` is the one exception.
- Primary constructors for services, with the parameters used directly in the body (`RepositoryCachePruner`, `CacheListCommand`).
- `record` for data (`ProjectMetadata`, `InstallationRecord`, `CachedRepositoryInfo`) and `class` for behavior.
- Collection expressions (`[]`), target-typed `new`, and camelCase private fields with no underscore prefix.
- A `CancellationToken` parameter on every async method that reaches a process, the filesystem, or the network.

Failures travel as a `CliException` carrying an error kind and an exit code, and reach human output and the JSON envelope through `ICliOutput`. Keep new failures on that path instead of throwing raw exceptions or writing to the console directly.

## Tests

Tests live in `tests/DotnetGitTool.Tests`, one class per area, alongside evaluation-only fixture data in `tests/Fixtures/SimpleTool`. [Architecture](docs/architecture.md#tests) describes the suite layout, the framework versions, and the fixture. What follows is what a contributor has to match when adding a test.

- Every test is a `[Fact]`. The suite contains no `[Theory]`.
- Test names are behavior sentences in PascalCase with no underscores: `ExplicitRefOverridesEmbeddedRef`, `RefusesPathOutsideRepositoryRoot`, `SkipsRepositoryLockedByAnotherOperation`.
- Multi-step bodies separate arrange, act, and assert with blank lines, without `// Arrange` comments. A one-assertion test may be expression-bodied, as `PackageIdentityTests.PackageIdIsStableAndNuGetSafe` is.
- Anything touching the filesystem allocates a temp directory with `Directory.CreateTempSubdirectory`, prefixed `dotnet-git-tool-<AREA>-tests-` (two older call sites in `CliProcessTests` use the plain `dotnet-git-tool-tests-`), and deletes it in `Dispose` or `finally`. Clear read-only attributes before deleting, because Git object files are read-only.
- A test that spawns the built tool and can reach the repository cache or the installation state file sets `DOTNET_GIT_TOOL_CACHE` and `DOTNET_GIT_TOOL_HOME` to temp directories, as `CliProcessTests.CachePruneEnvironment` does. The child-process tests that omit them print help, fail argument validation, or return from the `--dry-run` branch, so none of them writes to a cache or a state file.

Ship tests with behavior changes. Flag names, exit codes, error kinds, and the JSON envelope are asserted deliberately, so renaming a flag or changing an exit code breaks a test on purpose. Update the test and the documentation in the same change.

`ProjectDiscoveryTests` locates the fixture through a relative path from `AppContext.BaseDirectory`, so moving `tests/Fixtures/SimpleTool` or changing the test project's output layout breaks it.

## Documentation

Behavior changes ship with documentation changes in the same pull request.

- A new or renamed flag, a changed output shape, a new exit code or error kind, or a changed default belongs in the [CLI reference](docs/cli-reference.md) first, then in every other page that mentions it.
- Each page owns one topic and links instead of duplicating. The [documentation index](docs/README.md) lists the owners and holds the glossary.
- Use the glossary terms (source tool, source ID, repository cache, installation record, command style, JSON envelope, repository manifest) instead of inventing synonyms.
- Every page under `docs/` ends with a `## See also` section whose first bullet is `- [Documentation index](README.md)`.
- Every code fence carries a language tag: `console` for commands, `text` for captured output, plus `json`, `xml`, `bash`, `powershell`, `yaml`, and `csharp` where they apply. Commands and output never share a fence.
- `README.md` is packed into the NuGet package through `<PackageReadmeFile>README.md</PackageReadmeFile>`, so every link in it must be an absolute `https://github.com/JKamsker/dotnet-git-tool/...` URL. Relative links break on nuget.org.
- Copy sample output from a real run. Do not hand-write output the program never printed.

## Commits and pull requests

Subject lines in this repository are short, capitalized, imperative, and carry no prefix and no trailing period: `Add cache inspection commands`, `Fix empty tool command fallback`, `Publish the tool through trusted NuGet releases`. Larger commits add a body of one or two paragraphs saying what changed and why. Keep one logical change per commit.

Work on a branch, never on `main`, and push the branch to your fork:

```console
git switch -c <BRANCH_NAME>
git push -u origin <BRANCH_NAME>
```

Open pull requests against `main`. A useful description contains:

- What changed and why, in the reader's terms rather than the diff's.
- Any user-visible change to flags, output, exit codes, error kinds, or the JSON envelope.
- The commands you ran to verify it, including the test command.
- The documentation pages you updated.
- Anything left out of scope on purpose.

CI has to be green before a merge. Because every push to `main` publishes a release, `main` stays releasable at all times.

## Continuous integration and releases

[.github/workflows/ci.yml](.github/workflows/ci.yml) runs on pushes to `main`, pull requests targeting `main`, and manual dispatch. The `build-test-package` job runs on `ubuntu-latest`:

1. Check out with `fetch-depth: 0`.
2. Set up .NET.
3. `dotnet restore dotnet-git-tool.slnx`.
4. Build Release.
5. Test Release.
6. Resolve the package version with `eng/Resolve-PackageVersion.ps1`.
7. Pack `src/DotnetGitTool/DotnetGitTool.csproj` into `artifacts/`.
8. Upload the `.nupkg` as a workflow artifact named `JKToolKit.Git.Tool-<VERSION>`.

The `publish` job runs only on a push to `main`. It uses the `production` environment, downloads the artifact the first job uploaded rather than rebuilding, exchanges the workflow's GitHub identity for a temporary NuGet API key through NuGet trusted publishing, and pushes to nuget.org with `--skip-duplicate`. There is no manual release step and no stored API key.

The version comes from [eng/Resolve-PackageVersion.ps1](eng/Resolve-PackageVersion.ps1), which you can run yourself:

```console
pwsh ./eng/Resolve-PackageVersion.ps1
```

It prints `0.0.<NUMBER_OF_COMMITS_AFTER_THE_BASELINE_TAG>`. On a clone without the tag it throws with this message instead:

```text
The package-version baseline tag 'nuget-v0.0.0' is missing. Fetch the complete Git history and tags.
```

The baseline tag name is the `-BaseTag` parameter and defaults to `nuget-v0.0.0`. The script also throws when `HEAD` sits on the tag, so `0.0.1` is the first publishable version. Three consequences:

1. Every push to `main` publishes one release. The patch number is the total commit count after the baseline tag, so a merge that lands three commits raises the patch by three and publishes once.
2. The history and the `nuget-v0.0.0` tag must stay intact. Do not force-push `main`, rewrite history, or delete the tag.
3. `<Version>0.0.1</Version>` in `src/DotnetGitTool/DotnetGitTool.csproj` is only the local default. CI overrides it with `-p:Version=`.

## Reporting bugs and proposing features

Open an entry on the [issue tracker](https://github.com/JKamsker/dotnet-git-tool/issues). For a bug, include:

- The exact command with every flag, and the exit code you got.
- The output. `--json` writes a structured envelope to stdout once the command starts running; argument and usage errors instead print a plain `error:` line on stderr and leave stdout empty, so paste both streams. `--verbose` adds the tool's own diagnostic lines on stderr in human output.
- The output of `dotnet git-tool --version` and `dotnet --version`, plus your operating system.
- The repository you targeted, if it is public, and the ref.
- For a failed build or pack, the real error. A failed `git` or `dotnet` run surfaces only its last output line. If the repository reached the repository cache, run `dotnet git-tool cache show <REPOSITORY>` for its path, then in that directory run `dotnet pack <PATH> --configuration Release -p:PackAsTool=true` and paste the full output. If the clone itself failed, paste the `error:` line as is.

For a feature, describe the problem you hit and the workaround you use today, then the change you want. Say which command it belongs to and whether it changes the JSON envelope, an exit code, or an error kind, because other people script against those.

There is no private disclosure channel today. A security report goes to the same issue tracker; say up front that it is a security issue so it can be triaged first.

## License

This repository is under the MIT license (see [LICENSE](LICENSE)), and contributions are accepted under that same license. There is no contributor license agreement and no Developer Certificate of Origin sign-off requirement, so your commits need no `Signed-off-by` trailer.
