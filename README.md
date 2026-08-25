# dotnet-git-tool

[![NuGet](https://img.shields.io/nuget/v/JKToolKit.Git.Tool?logo=nuget&label=NuGet)](https://www.nuget.org/packages/JKToolKit.Git.Tool) [![Downloads](https://img.shields.io/nuget/dt/JKToolKit.Git.Tool?label=downloads)](https://www.nuget.org/packages/JKToolKit.Git.Tool) [![CI](https://img.shields.io/github/actions/workflow/status/JKamsker/dotnet-git-tool/ci.yml?branch=main&label=CI)](https://github.com/JKamsker/dotnet-git-tool/actions/workflows/ci.yml) [![License](https://img.shields.io/badge/license-MIT-blue)](https://github.com/JKamsker/dotnet-git-tool/blob/main/LICENSE)

`cargo install --git` or `go install`, for .NET global tools. `dotnet-git-tool` installs a .NET command-line tool straight from a Git repository: it clones the repository, discovers the tool project, packs it, and installs the result as a .NET global tool.

## What it does

A useful .NET CLI lives on GitHub, but its author never pushed it to NuGet.org. Or the fix you need is on `main` and the last release is six months old, or the repository is internal and will never be published at all. Normally each of those means cloning, building, packing, and wiring up a local feed by hand. `dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`) replaces that whole sequence with one command, once it is installed itself:

```console
dotnet git-tool install JKamsker/bookmeta-cli
```

It says what it is about to build and asks once:

```text
Warning: building 'JKamsker/bookmeta-cli' can execute arbitrary code from that repository.
Continue? [y/N]
```

Answer `y` and that repository's tool is installed globally, ready to run as `dotnet bookmeta`. The target repository needs no changes when exactly one of its projects qualifies: an ordinary console project with `<OutputType>Exe</OutputType>` is enough, because the packer passes `PackAsTool=true` itself, and when more than one project qualifies `dotnet-git-tool` refuses to guess and you pass `-p, --project`. A tool installed this way is a **source tool**: a global tool built from a Git repository instead of downloaded from a NuGet feed. `dotnet-git-tool` records every source tool it installs, and a recorded installation is **managed**.

## Requirements

- A .NET 10 SDK. `dotnet-git-tool` targets `net10.0` and runs `dotnet pack`, so a runtime-only machine cannot build a source tool.
- `git` and `dotnet` on `PATH`. If either is missing, the command fails with `Could not start 'git'. Make sure it is installed and available on PATH.` and exit code 5.
- Windows, macOS, and Linux.

## Quickstart

Install `dotnet-git-tool` itself from NuGet.org:

```console
dotnet tool install --global JKToolKit.Git.Tool
```

The package ID is `JKToolKit.Git.Tool` and the packaged command is `dotnet-git-tool`, so the `dotnet` driver runs it as `dotnet git-tool`. `dotnet tool install --global` puts the executable in the .NET global tools directory, which has to be on `PATH`, so open a new shell before you confirm the install with `dotnet git-tool --version`, which prints the version and exits 0. If it is still not found, [Troubleshooting](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/troubleshooting.md#dotnet-git-tool-is-not-found-after-installing-the-package) has the fix. A separate, unrelated package published on NuGet.org under the ID `dotnet-git-tool` is not this project, and because both expose the command `dotnet-git-tool` only one of them can be installed globally at a time; `dotnet tool list --global` prints the package ID next to the command it exposes.

Preview an installation first with `dotnet git-tool install JKamsker/bookmeta-cli --dry-run`, which clones nothing, executes no repository code, and prints the plan it would follow. Then install it for real. Answer `y` at the confirmation prompt, which is your consent that building this repository runs its code on your machine under your account (see Safety below), or pass `--yes` to confirm up front, which is what scripts and CI do:

```console
dotnet git-tool install JKamsker/bookmeta-cli
```

Output. Paths in the examples use the Linux defaults, and the commits, versions, dates, and command name are illustrative: the command name comes from the target repository's own project, so read the real one from the `Command:` clause of the success line or the `COMMAND` column of `dotnet git-tool list`.

```text
Installed JKamsker/bookmeta-cli at 4fbe47e66359. Command: dotnet bookmeta. Clean sources: /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

The tool is now installed globally and runs as `dotnet bookmeta`. Progress lines go to stderr, and output from `git` and MSBuild is captured rather than streamed, so a long clone or build prints nothing until it finishes. If a build fails you get one line from it and exit code 1: run `dotnet git-tool cache show JKamsker/bookmeta-cli` for the path of the retained cached repository and run `dotnet pack` there yourself to see the real error, or start at [Troubleshooting](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/troubleshooting.md).

Private repositories work when your existing Git credential helper or SSH agent answers without prompting, because `dotnet-git-tool` never handles credentials itself: repository access goes through `git`, and the build restores NuGet packages through `dotnet`. A helper that needs to prompt has nowhere to prompt, so the command appears to hang. Authenticate with `git` once outside `dotnet-git-tool` first.

To see what you have installed:

```console
dotnet git-tool list
```

Output:

```text
SOURCE                         PACKAGE                            COMMIT         COMMAND                  CACHE PATH
JKamsker/bookmeta-cli          git.JKamsker.bookmeta-cli          4fbe47e663597… dotnet bookmeta          /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

Rebuild from the latest commit with `dotnet git-tool update JKamsker/bookmeta-cli --yes`, and remove the tool again with `dotnet git-tool uninstall JKamsker/bookmeta-cli --yes`. An update that finds no new commit reports that the tool is already at that commit and reinstalls nothing. Uninstalling removes the global tool and its installation record, and keeps the cached repository. For a slower walkthrough, read the [Getting started guide](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/getting-started.md).

## How it works

`install` and `update` run the same pipeline. It starts by parsing the repository argument into a clone URL and a source ID such as `JKamsker/bookmeta-cli`, then:

1. Clone the repository into the repository cache with `git clone --depth 1 --no-tags`, or reuse and reset the cached repository already there.
2. Discover the project to build by evaluating candidate `.csproj` files with MSBuild.
3. Pack it with `dotnet pack` into a temporary directory, using a generated package ID such as `git.JKamsker.bookmeta-cli` and a generated version such as `0.0.0-git.4fbe47e66359.dotnet`, then install it with `dotnet tool install --global`.
4. Write the installation record and reset the cached repository to a clean checkout.

Nothing is uploaded anywhere. The generated package is built into a temporary directory, installed from there, and the temporary directory is removed; the installed copy lives in the .NET global tool store like any other global tool. [How it works](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/how-it-works.md) covers discovery, package identity, and the SDK fallback in full.

## Commands

| Command | Purpose |
|---|---|
| `dotnet git-tool install <REPOSITORY>` | Clone, discover, pack, and globally install a tool from source. |
| `dotnet git-tool update <REPOSITORY>` | Rebuild and update a previously installed source tool. |
| `dotnet git-tool uninstall <REPOSITORY>` | Uninstall a source tool and remove its recorded state. |
| `dotnet git-tool list` | List source tools managed by dotnet git-tool. |
| `dotnet git-tool cache list` | List cached repositories in a compact source, version, and date table. |
| `dotnet git-tool cache show <REPOSITORY>` | Show Git, package, state, size, and path details for a cached repository. |
| `dotnet git-tool cache prune` | Remove cached repositories not used by managed installations. |

For `install`, `update`, and `uninstall`, `<REPOSITORY>` accepts `owner/repo`, `owner/repo@ref`, `git@github.com:owner/repo.git`, or a full HTTP(S) or SSH URL. The `owner/repo` and `git@github.com:` forms are GitHub-only; any other host needs a full URL. The `@ref` suffix works only on the `owner/repo` form, so pin a URL or an SSH address with `--ref <REF>` instead. For `cache show`, the same placeholder means something else: a source ID, a repository name, a generated package ID, or a cache directory name, never a clone URL and never an `@ref`.

`--dry-run` and `-y, --yes` gate `install`, `update`, `uninstall`, and `cache prune`, and `--dry-run` wins when the two are combined. `--json`, `--quiet`, `--verbose`, and `--no-color` work on every subcommand in the table above, though `--quiet` never silences the output of `list`, `cache list`, or `cache show`. `--no-color` is accepted for compatibility and has no effect today; `dotnet-git-tool` renders the `cache list` table through Spectre.Console and Spectre.Console.Cli renders the help and version screens, so the `NO_COLOR` environment variable reaches both, while every other surface is plain text already. Every option, argument, exit code, and error kind is in the [CLI reference](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/cli-reference.md).

## Two command styles

By default a discovered base name such as `bookmeta` is packaged as `dotnet-bookmeta`, so you invoke it through the `dotnet` driver as `dotnet bookmeta`. That is dotnet style. Standalone style exposes the base name with no prefix. Pass `--standalone` at install time, or on an `update` to switch an installation that already exists:

```console
dotnet git-tool install JKamsker/bookmeta-cli --yes --standalone
dotnet git-tool update JKamsker/bookmeta-cli --yes --standalone
```

Both styles install into the .NET global tools directory and both need that directory on `PATH`: the `dotnet` driver resolves `dotnet bookmeta` by finding a `dotnet-bookmeta` executable on `PATH`, exactly as your shell resolves `bookmeta`. What changes is the name you type and what it can collide with, so prefer the default when you want the tool grouped under `dotnet` and out of the way of same-named executables on `PATH`, and prefer standalone style when the command should be typed on its own. `update` reuses the recorded style unless you pass `--standalone` or `--dotnet-command` (the two flags are mutually exclusive), and the style is part of the generated package version, so changing it always repacks, even at the same commit. If an installed command is not found afterwards, [Troubleshooting](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/troubleshooting.md#the-installed-command-is-not-found-but-the-install-succeeded) covers it.

## Safety

> [!WARNING]
> Installing or updating builds the target repository on your machine. MSBuild evaluation, restore, build, and pack execute arbitrary code from that repository under your user account.

- `--dry-run` reports the plan from local state only. It clones nothing, executes no repository code, and never prompts.
- `install`, `update`, `uninstall`, and `cache prune` require a confirmation at the terminal or `--yes`. Under `--json`, `--quiet`, or a redirected stdin or stderr they refuse with exit code 2 instead of prompting, except for a `cache prune` that has nothing to remove.
- Pin what you install by appending a ref, `dotnet git-tool install JKamsker/bookmeta-cli@<REF>`, or by passing `--ref <REF>`. Clones are shallow, so branches and tags resolve by name while an arbitrary old commit may not be reachable.
- An `update` without a ref switches to the remote default branch and pulls its latest commit. Pass a new `@ref` or `--ref` to remain pinned or move to another ref.
- The cached repository is retained and inspectable. `dotnet git-tool cache show JKamsker/bookmeta-cli` prints its path, so you can read the code that was built.
- Misspelled options are ignored rather than rejected, so `--dryrun --yes` performs a real installation. A genuine preview says `Would prepare` and installs nothing, so check for that line before you trust a dry run.

The threat model and every condition that refuses a confirmation are in [Security](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/security.md).

## Limitations

- Only `.csproj` projects are discovered. `.fsproj` and `.vbproj` are not supported.
- There is no way to install from a directory on disk or a `file://` URL. Only `https`, `http`, and `ssh` URLs are accepted, plus the two GitHub-only forms above.
- Clones are shallow and carry no tags, so a build that derives its version from history or tags does not get either.
- Submodules are never initialized, so a repository that needs submodule content to build fails.
- No option prints a failing build log. `--verbose` adds `dotnet-git-tool`'s own diagnostic lines and nothing from `git` or MSBuild.
- There is no command that updates every source tool at once, and every install is a `--global` install.

## Where things live

`dotnet-git-tool` writes to exactly two places: the repository cache, which holds one cached repository per source tool, and the installation state file `installed.json`. On Linux with no environment variables set those are `/home/you/.cache/dotnet-git-tool` and `/home/you/.local/share/dotnet-git-tool/installed.json`. macOS uses `~/.cache/dotnet-git-tool` for the cache, not `~/Library/Caches`, while its state file sits under `~/Library/Application Support`.

`DOTNET_GIT_TOOL_CACHE`, `DOTNET_GIT_TOOL_HOME`, `XDG_CACHE_HOME`, and `XDG_DATA_HOME` all move those two locations, on every platform including Windows; give each an absolute path, because a relative value resolves against the current working directory. The installed tools themselves land wherever `dotnet tool install --global` puts them, and `uninstall` keeps the cached repository on purpose, so a later reinstall can reuse it. [Configuration](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/configuration.md) lists the Windows and macOS defaults and both precedence chains, and [Repository cache](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/repository-cache.md) covers the cache layout.

To remove everything `dotnet-git-tool` put on your machine:

1. Run `dotnet git-tool uninstall <REPOSITORY> --yes` for each source tool that `dotnet git-tool list` reports.
2. Run `dotnet git-tool cache prune --yes`.
3. Delete the cache root directory and the state directory that holds `installed.json`.
4. Run `dotnet tool uninstall --global JKToolKit.Git.Tool`.

## Scripting and CI

`--json` writes one `{ok, data, error, meta}` envelope to stdout, and on failure `ok` is `false`, `data` is `null`, and `error` carries a `kind` and a `message`. Stderr stays empty for anything the command itself reports, but argument and unknown-command errors bypass the envelope and print a plain `error:` line to stderr with empty stdout, so treat an empty stdout plus a non-zero exit code as a failure. `--json` never prompts, so pair it with `--yes` for any mutation.

The envelope and every command's `data` keys are in the [CLI reference](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/cli-reference.md#the-json-envelope). [Automation](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/automation.md) has `jq` recipes, exit-code branching, and a GitHub Actions example.

## If you maintain a .NET CLI

A repository with one console project needs no changes, so the line to put in your own README is `dotnet git-tool install <REPOSITORY>` with your `owner/repo` in place of the placeholder. To control which project is built and what the command is called, commit `.config/dotnet-git-tool.json` with a `project` field, a `command` field, or both. This is not a .NET tool manifest (`dotnet-tools.json`); `dotnet-git-tool` does not read or write those. [Authoring tools](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/authoring-tools.md) has the discovery rules, the manifest schema, and how to verify your repository with `--dry-run`.

## Documentation

- Start here: the [documentation index](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/README.md), which maps every page and defines the terms used across them.
- Using it: [Getting started](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/getting-started.md), [CLI reference](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/cli-reference.md), [Troubleshooting](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/troubleshooting.md).
- Understanding it: [How it works](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/how-it-works.md), [Repository cache](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/repository-cache.md), [Configuration](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/configuration.md), [Security](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/security.md).
- Extending it: [Authoring tools](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/authoring-tools.md) for tool authors, [Automation](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/automation.md) for CI, [Architecture](https://github.com/JKamsker/dotnet-git-tool/blob/main/docs/architecture.md) and [Contributing](https://github.com/JKamsker/dotnet-git-tool/blob/main/CONTRIBUTING.md) for working on this repository, including building, testing, packing locally, and the version scheme.

## License

MIT, by [JKamsker](https://github.com/JKamsker). See [LICENSE](https://github.com/JKamsker/dotnet-git-tool/blob/main/LICENSE). Report problems at [Issues](https://github.com/JKamsker/dotnet-git-tool/issues).
