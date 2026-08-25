# Documentation index

`dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`) builds a .NET global tool from a Git repository and installs it, so you can run a tool whose author never published it to a NuGet feed. This page maps the documentation and defines the terms it uses. Start from the path that matches what you are doing rather than reading straight through.

These pages assume `dotnet-git-tool` is installed already. The [project README](../README.md) has the single command that installs it.

## Start here

| If you want to | Read |
|---|---|
| Install a tool someone else wrote | [Getting started](getting-started.md) first, then [Security](security.md), [CLI reference](cli-reference.md), and [Troubleshooting](troubleshooting.md) |
| Make your own repository installable | [Authoring a tool repository](authoring-tools.md), [How it works](how-it-works.md), and [Repository cache](repository-cache.md) |
| Script it in CI | [Automation](automation.md), [CLI reference](cli-reference.md), [Configuration](configuration.md), and [Security](security.md) |

## Documents

| Document | What it covers | Who it is for |
|---|---|---|
| [Getting started](getting-started.md) | A first install, from prerequisites through `--dry-run`, `install`, `list`, `update`, and `uninstall`. | Anyone running `dotnet-git-tool` for the first time. |
| [CLI reference](cli-reference.md) | Every command, argument, and option, the repository argument grammar, exit codes, error kinds, and the JSON envelope. | Anyone who needs the exact command and option details. |
| [How it works](how-it-works.md) | The install pipeline end to end, generated package ID and version derivation, and the SDK fallback that retries when a repository pins an SDK you do not have. | Readers who want to know what happens between clone and installed command. |
| [Authoring a tool repository](authoring-tools.md) | Project requirements, project discovery from the author's side, the repository manifest, and command name resolution. | Tool authors. |
| [Repository cache](repository-cache.md) | Cache layout, how a source ID maps to a fixed cache directory, the repository lock, the clean guarantee, and the three `cache` commands. | Anyone inspecting or reclaiming cached repositories. |
| [Configuration](configuration.md) | Environment variables and their precedence, the per-platform default cache and state paths, and the shape of `installed.json`. | Anyone relocating or isolating what the tool writes. |
| [Automation](automation.md) | Running unattended: non-interactive flags, parsing the envelope with `jq`, pinning refs, and isolating state in CI. | CI pipelines, provisioning scripts, dotfiles bootstraps. |
| [Security](security.md) | The threat model, the confirmation prompt, `--dry-run` as a preview, pinning refs, and credential handling. | Anyone deciding whether to trust a repository. |
| [Troubleshooting](troubleshooting.md) | Symptom, cause, and fix for the error kinds you are likely to hit, keyed by the message text. | Anyone whose command failed. |
| [Architecture](architecture.md) | The source tree map, the Spectre.Console.Cli wiring, the settings inheritance chain, the interfaces the tests substitute, and the test layout. | Contributors to this repository. |

## Common tasks

Commands that take a repository use the same example repository, `JKamsker/bookmeta-cli`. `--yes` skips the confirmation prompt, and building a repository can execute arbitrary code from it; [Security](security.md) explains what runs.

| Task | Command or setting | Explained in |
|---|---|---|
| Preview an install without cloning or building anything | `dotnet git-tool install JKamsker/bookmeta-cli --dry-run` | [Security](security.md) |
| Pin an install to a tag | `dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0 --yes` | [CLI reference](cli-reference.md) |
| Switch from dotnet style to standalone style | `dotnet git-tool update JKamsker/bookmeta-cli --standalone --yes` | [How it works](how-it-works.md) |
| Diagnose a failed build | `dotnet git-tool cache show JKamsker/bookmeta-cli` | [Troubleshooting](troubleshooting.md) |
| Preview which cached repositories can be removed | `dotnet git-tool cache prune --dry-run` | [Repository cache](repository-cache.md) |
| Reclaim disk from cached repositories nothing uses | `dotnet git-tool cache prune --yes` | [Repository cache](repository-cache.md) |
| Move the repository cache somewhere else | `DOTNET_GIT_TOOL_CACHE` | [Configuration](configuration.md) |
| Read a result from a script | `dotnet git-tool list --json` | [Automation](automation.md) |

## Glossary

These are the terms the documentation and the program's own output use. The last column names the document that defines each one in full.

| Term | Meaning | Defined in |
|---|---|---|
| source tool | A .NET global tool that `dotnet-git-tool` built from a Git repository rather than installing it from a NuGet feed. | [Getting started](getting-started.md) |
| managed | Recorded in the installation state file (`installed.json`). A cached repository with no matching installation record is unmanaged. | [Configuration](configuration.md) |
| repository argument | The `<REPOSITORY>` value you pass to `install`, `update`, and `uninstall`, written `owner/repo`, `owner/repo@ref`, an SSH URL, or an HTTP(S) URL. | [CLI reference](cli-reference.md) |
| `cache show` selector | The `<REPOSITORY>` value `cache show` takes: a source ID, a repository name, a generated package ID, or a cache directory name. It accepts neither a clone URL nor an `@ref` suffix. | [Repository cache](repository-cache.md) |
| source ID | The normalized identity derived from the repository argument, for example `JKamsker/bookmeta-cli`. It names the cache directory, the generated package, and the installation record. | [CLI reference](cli-reference.md) |
| requested ref | The branch, tag, or commit you pinned, written `JKamsker/bookmeta-cli@v1.2.0` or `--ref v1.2.0`. | [CLI reference](cli-reference.md) |
| revision | The `Revision` field `cache show` prints, from `git describe --tags --always --dirty`. It describes the commit that is checked out, not the ref you asked for. | [Repository cache](repository-cache.md) |
| command style | Whether the installed command is invoked as `dotnet bookmeta` (dotnet style, the default for `install`) or as `bookmeta` (standalone style). | [How it works](how-it-works.md) |
| base name | The command name before prefixing, for example `bookmeta`. | [How it works](how-it-works.md) |
| generated package ID | The ID of the NuGet package `dotnet-git-tool` produces, for example `git.JKamsker.bookmeta-cli`. | [How it works](how-it-works.md) |
| the repository cache | The directory tree of cached repositories. Each one lives in its own cache directory under `<CACHE_ROOT>/repositories`. | [Repository cache](repository-cache.md) |
| the clean guarantee | The rule that `dotnet-git-tool` resets and cleans a cached repository, so the sources it retains stay unmodified. | [Repository cache](repository-cache.md) |
| installation record | One entry in the installation state file (`installed.json`) describing an installed source tool. | [Configuration](configuration.md) |
| the JSON envelope | The object `--json` writes to stdout once a command starts running, with `ok`, `data`, `error`, and `meta`. Argument errors print to stderr instead. | [CLI reference](cli-reference.md) |
| repository manifest | `.config/dotnet-git-tool.json`, committed by a tool author to select the project and the command name. This is not a .NET tool manifest (`dotnet-tools.json`); `dotnet-git-tool` does not read or write those. | [Authoring a tool repository](authoring-tools.md) |

## See also

- [Project README](../README.md), the short pitch and the install instructions for `dotnet-git-tool` itself
- [Contributing](../CONTRIBUTING.md), for building, testing, and releasing this repository
- [Getting started](getting-started.md), if you have not installed anything yet
