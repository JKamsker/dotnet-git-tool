# dotnet-git-tool

`dotnet-git-tool` builds and installs .NET global tools directly from Git repositories.

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
dotnet git-tool install JKamsker/bookmeta-cli --yes
dotnet git-tool install JKamsker/bookmeta-cli --yes --standalone
dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0 --yes
dotnet git-tool update JKamsker/bookmeta-cli --yes
dotnet git-tool uninstall JKamsker/bookmeta-cli --yes
dotnet git-tool list
dotnet git-tool cache list
dotnet git-tool cache show JKamsker/bookmeta-cli
dotnet git-tool cache prune --dry-run
dotnet git-tool cache prune --yes
```

> [!WARNING]
> MSBuild evaluation, restore, build, and pack can execute arbitrary code from a repository. Review the source and use a pinned ref when appropriate. Mutating build operations require an interactive confirmation or `--yes`; `--dry-run` previews without cloning or executing repository code.

## Install

Install the published tool from NuGet.org:

```console
dotnet tool install --global JKToolKit.Git.Tool
```

The package ID is `JKToolKit.Git.Tool`, while its command is named `dotnet-git-tool`. The .NET driver therefore invokes it as `dotnet git-tool`.

To build and install this repository locally instead:

```console
dotnet pack src/DotnetGitTool -c Release -o artifacts
dotnet tool install --global JKToolKit.Git.Tool --add-source artifacts
```

Packages are published from every push to `main` through NuGet trusted publishing. Versions start at `0.0.1`; the patch component is the number of commits after the immutable `nuget-v0.0.0` baseline tag, so each new commit advances it by one.

## Installed command style

By default, a discovered command such as `bookmeta` is packaged as `dotnet-bookmeta`, so it is invoked through the .NET driver:

```console
dotnet git-tool install JKamsker/bookmeta-cli --yes
dotnet bookmeta --help
```

Use `--standalone` to expose the command without the `dotnet` prefix:

```console
dotnet git-tool install JKamsker/bookmeta-cli --yes --standalone
bookmeta --help
```

Both modes are global .NET tool installations; `--standalone` changes only the command name. Updates preserve the recorded style. Pass `--standalone` or `--dotnet-command` to `update` to change an existing installation. The explicit flags are mutually exclusive.

## Project discovery

The installer evaluates `.csproj` files through MSBuild and chooses a project in this order:

1. `--project <PATH>`
2. `.config/dotnet-git-tool.json`
3. exactly one project with `PackAsTool=true`
4. exactly one project with `OutputType=Exe`

Ambiguity is an error. A directory passed to `--project` must contain exactly one project file. The optional manifest is:

```json
{
  "project": "src/BookMeta.Cli/BookMeta.Cli.csproj",
  "command": "bookmeta"
}
```

Normal console projects are packed with `PackAsTool=true`. Generated packages use an ID such as `git.JKamsker.bookmeta-cli` and a commit/style-derived version such as `0.0.0-git.0123456789ab.dotnet`.

MSBuild evaluation and packing initially honor the source repository's `global.json`. If SDK resolution fails and a strictly newer .NET SDK is already installed, `dotnet-git-tool` retries from an isolated working directory so the newer installed SDK can build the project. It does not retry with an older SDK or retry ordinary restore, compilation, or pack failures.

## Repository cache

Source repositories are cloned once and retained in a cache. Install and update both reuse the same working tree; update fetches the remote default branch or requested ref and resets the cache to the fetched commit without creating local merge commits.

After every MSBuild evaluation and pack attempt—including failures—the cache is restored with `git reset --hard HEAD` and `git clean -ffdx`. Initialized submodules are reset and cleaned recursively, and a final porcelain-status check must be empty. Package files are built in a separate temporary directory, so the retained repository contains only tracked source files.

Cache location precedence is:

1. `DOTNET_GIT_TOOL_CACHE`
2. `$XDG_CACHE_HOME/dotnet-git-tool`
3. `~/.cache/dotnet-git-tool` on Unix, or the platform local application-data cache on Windows

Each source identity maps to a deterministic directory under `repositories/`, with a per-repository lock to prevent concurrent builds from sharing a working tree. Uninstall retains the clean source cache so a later reinstall can reuse it.

Inspect every retained repository with `dotnet git-tool cache list`. Human output is a compact Spectre.Console table containing only source, `[source-version|12-character-commit]`, installation date, and publication/commit date. `--json` returns the complete inventory—including full paths and detailed Git and package metadata—in the stable v1 envelope.

Use `dotnet git-tool cache show <REPOSITORY>` for detailed origin, branch, commit, revision, commit date, clean/dirty status, disk size, package, command, project, installation/update dates, and full-path information. A repository can be selected by exact source ID, repository name, package ID, or cache directory name. Exact source IDs take precedence; ambiguous short names are rejected with the matching source IDs.

Use `dotnet git-tool cache prune --dry-run` to list repository directories that are not referenced by managed installations. Run `dotnet git-tool cache prune --yes` to remove them. Pruning preserves managed repositories and skips repositories locked by another install, update, or prune operation. Cache-root resolution uses the precedence above; the command does not inspect or delete directories outside that resolved `repositories/` folder.

## Resolution and automation

Resolution precedence is command flags (`--ref`, `--project`, command-style flags) → embedded `owner/repo@ref` → recorded update settings → repository manifest → project conventions → defaults. New installations default to the `dotnet <command>` style. Updates reuse their recorded ref, project, and command style unless explicitly overridden; there is no implicit switch to another target.

Managed installation state includes the clone URL, cached repository path, project, package ID, requested ref, command style, and installed commit. Its location is resolved as:

1. `DOTNET_GIT_TOOL_HOME/installed.json`
2. `$XDG_DATA_HOME/dotnet-git-tool/installed.json`
3. the platform local application-data directory under `dotnet-git-tool/installed.json`

Human output is the default. `--json` selects a stable envelope with `ok`, `data`, `error`, and `meta.schemaVersion` (currently `1`). JSON data is written to stdout without progress chatter; human progress and errors use stderr. `list` renders a table in human mode and includes each managed repository's complete cache path.

Errors are concise and actionable by default. `--verbose` adds resolved clone, project, commit, package, and command details to stderr in human mode; JSON output remains envelope-only.

`--quiet` suppresses human progress and never prompts. `--json` is also non-interactive. Use `--yes` for execution or `--dry-run` for a no-side-effect preview. `--dry-run` wins when combined with `--yes`. `NO_COLOR` and `--no-color` are accepted; current output is intentionally plain text.

Exit codes: `0` success, `1` execution failure, `2` usage/confirmation required, `5` not found, `6` conflict, and `10` cancellation.
