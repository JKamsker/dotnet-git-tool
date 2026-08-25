# dotnet-git-tool

`dotnet-git-tool` builds and installs .NET global tools directly from Git repositories.

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
dotnet git-tool install JKamsker/bookmeta-cli --yes
dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0 --yes
dotnet git-tool update JKamsker/bookmeta-cli --yes
dotnet git-tool uninstall JKamsker/bookmeta-cli --yes
dotnet git-tool list
```

> [!WARNING]
> MSBuild evaluation, restore, build, and pack can execute arbitrary code from a repository. Review the source and use a pinned ref when appropriate. Mutating build operations require an interactive confirmation or `--yes`; `--dry-run` previews without cloning or executing repository code.

## Install

Build this repository and install the tool:

```console
dotnet pack src/DotnetGitTool -c Release -o artifacts
dotnet tool install --global dotnet-git-tool --add-source artifacts
```

The command is named `dotnet-git-tool`, which makes the .NET driver invocation `dotnet git-tool` work.

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

Normal console projects are packed with `PackAsTool=true`. Generated packages use an ID such as `git.JKamsker.bookmeta-cli` and a commit-derived version such as `0.0.0-git.0123456789ab`.

## Resolution and automation

Resolution precedence is command flags (`--ref`, `--project`) → embedded `owner/repo@ref` → repository manifest → project conventions → defaults. For updates, a recorded ref and project are reused unless explicitly overridden; there is no implicit switch to another target.

Managed installation state includes the clone URL, project, package ID, requested ref, and installed commit. Its location is resolved as:

1. `DOTNET_GIT_TOOL_HOME/installed.json`
2. `$XDG_DATA_HOME/dotnet-git-tool/installed.json`
3. the platform local application-data directory under `dotnet-git-tool/installed.json`

Human output is the default. `--json` selects a stable envelope with `ok`, `data`, `error`, and `meta.schemaVersion` (currently `1`). JSON data is written to stdout without progress chatter; human progress and errors use stderr. `list` renders a table in human mode.

Errors are concise and actionable by default. `--verbose` adds resolved clone, project, commit, package, and command details to stderr in human mode; JSON output remains envelope-only.

`--quiet` suppresses human progress and never prompts. `--json` is also non-interactive. Use `--yes` for execution or `--dry-run` for a no-side-effect preview. `--dry-run` wins when combined with `--yes`. `NO_COLOR` and `--no-color` are accepted; current output is intentionally plain text.

Exit codes: `0` success, `1` execution failure, `2` usage/confirmation required, `5` not found, `6` conflict, and `10` cancellation.
