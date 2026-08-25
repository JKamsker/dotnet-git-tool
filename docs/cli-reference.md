# CLI reference

`dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`) exposes four top-level commands and one `cache` branch with three subcommands. This page documents every command, argument, option, exit code, error kind, and JSON key.

`dotnet-git-tool` runs `git` and `dotnet` as external commands, so both must be on `PATH` and a .NET SDK must be installed. See [Getting started](getting-started.md) for a first walkthrough.

Paths in the examples use the Linux defaults (`/home/you/.cache/dotnet-git-tool`). Commit hashes, versions, sizes, and dates in the output blocks are illustrative and differ on your machine. For the default paths on each platform and the environment variables that move them, see the [configuration reference](configuration.md).

---

## Synopsis

The general form is:

```text
dotnet git-tool [-h|--help] [-v|--version]
dotnet git-tool install   <REPOSITORY> [--ref <REF>] [-p|--project <PATH>] [--standalone|--dotnet-command] [--dry-run] [-y|--yes] [--json] [--quiet] [--verbose] [--no-color]
dotnet git-tool update    <REPOSITORY> [--ref <REF>] [-p|--project <PATH>] [--standalone|--dotnet-command] [--dry-run] [-y|--yes] [--json] [--quiet] [--verbose] [--no-color]
dotnet git-tool uninstall <REPOSITORY> [--dry-run] [-y|--yes] [--json] [--quiet] [--verbose] [--no-color]
dotnet git-tool list      [--json] [--quiet] [--verbose] [--no-color]
dotnet git-tool cache list  [--json] [--quiet] [--verbose] [--no-color]
dotnet git-tool cache show  <REPOSITORY> [--json] [--quiet] [--verbose] [--no-color]
dotnet git-tool cache prune [--dry-run] [-y|--yes] [--json] [--quiet] [--verbose] [--no-color]
```

There is no `--tool-path`, no `--local`, no `--force`, no `--source`, no `cache clear`, no `doctor` command, and no shell completion command.

Bare `dotnet git-tool` prints the root help screen on stdout and exits `0`. `dotnet git-tool cache` with no subcommand prints the branch help screen on stdout, writes nothing to stderr, and exits `1`.

## Commands

Descriptions are the ones the program registers for each command.

| Command | Description |
|---|---|
| [`install`](#dotnet-git-tool-install) | Clone, discover, pack, and globally install a tool from source. |
| [`update`](#dotnet-git-tool-update) | Rebuild and update a previously installed source tool. |
| [`uninstall`](#dotnet-git-tool-uninstall) | Uninstall a source tool and remove its recorded state. |
| [`list`](#dotnet-git-tool-list) | List source tools managed by dotnet git-tool. |
| `cache` | Inspect and maintain retained source repositories. |
| [`cache list`](#dotnet-git-tool-cache-list) | List cached repositories in a compact source, version, and date table. |
| [`cache show`](#dotnet-git-tool-cache-show) | Show Git, package, state, size, and path details for a cached repository. |
| [`cache prune`](#dotnet-git-tool-cache-prune) | Remove cached repositories not used by managed installations. |

`cache` is a branch, not a runnable command. Its three subcommands do the work.

## Help and version

`-h` and `--help` print a help screen on stdout, write nothing to stderr, and exit `0`. They work on the root command, on the `cache` branch, and on every subcommand.

`-v` and `--version` exist only on the root command. They print the assembly version rendered to three components:

```console
dotnet git-tool --version
```

Output (the value is whichever version you installed):

```text
0.0.1
```

## Global options

Every subcommand accepts these four options. The bare `dotnet git-tool` root command takes only `-h`/`--help` and `-v`/`--version`, and rejects anything else with `error: Unexpected option '<NAME>'.` and exit `1`. The `cache` branch takes only `-h`/`--help`, and answers any other option with the branch help screen on stdout and exit `1`, exactly as it answers a missing subcommand.

| Option | Description |
|---|---|
| `--json` | Emit the stable v1 JSON envelope. |
| `--quiet` | Suppress status output and never prompt. |
| `--verbose` | Show resolved source, project, commit, and package details. |
| `--no-color` | Disable ANSI styling (also honored through NO_COLOR). |

How they interact:

- `--json` writes one JSON envelope to stdout and nothing else, suppressing status lines, diagnostic lines, and the human message. It implies non-interactive, so an operation that needs confirmation fails with `confirmation_required` (exit `2`) instead of prompting.
- `--quiet` suppresses progress status lines, diagnostic lines, and the human success message of a real mutation, and it implies non-interactive exactly like `--json`. It does not suppress the human output of `list`, `cache list`, or `cache show`, and it does not suppress the human message of a `--dry-run` preview.
- `--verbose` adds diagnostic lines on stderr during a real `install` or `update`: the clone URL, the cache directory, the resolved commit, the selected project, and the generated package with its packaged command name and invocation. An `update` that reports `unchanged` emits the first three and returns. Every `--dry-run` emits none, and so do `uninstall`, `list`, `cache list`, `cache show`, and `cache prune`, because those paths contain no diagnostic call at all.
- `--verbose` is ignored under `--json` or `--quiet`, and it never reveals `git` or MSBuild output.
- `--no-color` is accepted for compatibility, is parsed and never read, and currently has no effect. The `NO_COLOR` environment variable is honored by Spectre.Console for the `cache list` table; all other output is plain text already.

> [!WARNING]
> Unknown and misspelled options on a subcommand are silently ignored rather than rejected. `dotnet git-tool install JKamsker/bookmeta-cli --dryrun --yes` performs a real installation, because `--dryrun` is discarded and `--yes` satisfies the confirmation. Confirm a preview by looking for the `Would prepare ...` line, or for `"action": "install"` in the JSON envelope.

## Mutation options

`install`, `update`, `uninstall`, and `cache prune` accept two more options.

| Option | Description |
|---|---|
| `--dry-run` | Preview the operation without cloning, building, or changing state. |
| `-y, --yes` | Confirm the requested mutation without prompting. |

Rules:

- `--dry-run` wins when both are passed. The preview returns before the confirmation is evaluated and before any repository is prepared, so `--dry-run --yes` changes nothing.
- `--dry-run` performs no network access, starts no `git` or `dotnet` process, and never prompts. For `install` and `update` it reports the deterministic cache directory and whether `<CACHE_DIRECTORY>/.git` already exists.
- `--dry-run` is not a guaranteed exit `0`. State-store checks run first, so `install --dry-run` on a managed source ID fails with `already_installed` (exit `6`), and `update --dry-run` or `uninstall --dry-run` on an unmanaged source ID fails with `installation_not_found` (exit `5`).
- `--dry-run` still validates the syntax of the repository argument, the ref, and the style flags, so `invalid_source`, `invalid_ref`, and `invalid_command_style` (exit `2`) can all occur. It validates nothing beyond that: it does not check that the repository exists, that the ref exists, or that `--project` points anywhere real.
- Confirmation is refused instead of prompted in four situations, and the command then fails with `confirmation_required` (exit `2`). See [Security](security.md#confirmation) for the list.
- Answering anything other than `y` or `yes` at the confirmation prompt fails with `cancelled` (exit `10`).

The three non-interactive refusal messages are:

```text
Building '<SOURCE_DISPLAY>' can execute arbitrary repository code. Inspect with --dry-run or explicitly consent with --yes.
Uninstalling '<SOURCE_DISPLAY>' requires confirmation. Inspect with --dry-run or confirm with --yes.
Removing <N> unused cached repositories requires confirmation. Inspect with --dry-run or explicitly confirm with --yes.
```

For `install` and `update`, `<SOURCE_DISPLAY>` is the source ID plus any requested ref, so a pinned install reports `JKamsker/bookmeta-cli@v1.2.0`. For `uninstall` it is the recorded source ID alone. For what executes and when, see the [security guide](security.md).

## The REPOSITORY argument

`install`, `update`, `uninstall`, and `cache show` all take a positional `<REPOSITORY>` argument. `install` and `update` parse it into a **clone URL**, a **source ID**, and an optional **requested ref**. `cache show` uses it as a selector against the repository cache instead.

### Accepted forms

| Form | Example | Clone URL | Source ID |
|---|---|---|---|
| `owner/repo` | `JKamsker/bookmeta-cli` | `https://github.com/JKamsker/bookmeta-cli.git` | `JKamsker/bookmeta-cli` |
| `owner/repo@ref` | `JKamsker/bookmeta-cli@v1.2.0` | `https://github.com/JKamsker/bookmeta-cli.git` | `JKamsker/bookmeta-cli` |
| GitHub SSH shorthand | `git@github.com:JKamsker/bookmeta-cli.git` | the input, unchanged | `JKamsker/bookmeta-cli` |
| HTTP(S) URL | `https://github.com/JKamsker/bookmeta-cli.git` | the input, unchanged | `JKamsker/bookmeta-cli` |
| SSH URL | `ssh://git@example.com/JKamsker/bookmeta-cli.git` | the input, unchanged | `example.com/JKamsker/bookmeta-cli` |

Rules:

1. The `owner/repo` form is GitHub-only. A bare `owner/repo` always expands to `https://github.com/owner/repo.git`.
2. The `git@host:owner/repo` shorthand matches `github.com` only. `git@example.com:JKamsker/bookmeta-cli` fails with `invalid_source`. Other hosts need a full URL such as `ssh://git@example.com/JKamsker/bookmeta-cli.git`.
3. Accepted URL schemes are `http`, `https`, and `ssh`. A URL path with fewer than two segments fails with `invalid_source` and the message `Repository URLs must include an owner and repository path.`
4. A trailing `.git` is trimmed from the repository name. A URL on `github.com` yields `owner/repo`; a URL on any other host yields `host/owner/repo`. The `owner/repo` form and the equivalent GitHub URL therefore address the same installation record and the same cache directory.
5. Sharing a cache directory is not the same as being interchangeable. The clone URL is recorded exactly as you typed it, and reusing a cached repository requires its recorded origin to match the requested clone URL character for character, ignoring case. Aiming `owner/repo` and `https://github.com/owner/repo` (no `.git`) at the same cache directory fails with `repository_cache_conflict` (exit `6`) and the message `Cache path '<PATH>' belongs to a different remote ('<ORIGIN>'). Move it aside and retry.`
6. Only the last two path segments of a URL form the source ID. Intermediate segments are dropped, so `https://example.com/group/JKamsker/bookmeta-cli.git` yields `example.com/JKamsker/bookmeta-cli`, and two repositories that differ only above the last two segments collapse onto one source ID, one generated package ID, and one cache directory.
7. Anything else fails with `invalid_source` (exit `2`) and the message `Repository must be an owner/repo GitHub slug or an HTTP(S)/SSH Git URL.`

`uninstall` does not require a parseable value. It normalizes the argument when it can and otherwise falls back to the trimmed raw string, then matches the installation record case-insensitively. All of `JKamsker/bookmeta-cli`, `https://github.com/JKamsker/bookmeta-cli`, `git@github.com:JKamsker/bookmeta-cli.git`, and `jkamsker/BOOKMETA-CLI` resolve to the same record.

### The requested ref

| Rule | Detail |
|---|---|
| Where `@ref` is parsed | Only the `owner/repo` form parses `@ref`. Appending `@ref` to a URL makes it part of the repository name, so `https://github.com/JKamsker/bookmeta-cli.git@v1.2.0` yields the source ID `JKamsker/bookmeta-cli.git@v1.2.0` instead of pinning anything. Appending it to `git@github.com:JKamsker/bookmeta-cli.git` fails with `invalid_source`. Use `--ref` with both forms. |
| Precedence | An explicit `--ref <REF>` overrides a ref embedded in `owner/repo@ref`. |
| Length | A ref longer than 1024 characters is rejected. |
| Leading `-` | A ref that starts with `-` is rejected. This is an argument-injection guard: it stops a value such as `--upload-pack=evil` from being reinterpreted by `git` as an option. |
| Whitespace | A ref containing any whitespace character is rejected. |
| Control characters | A ref containing any control character is rejected. |

A rejected ref fails with `invalid_ref` (exit `2`) and the message `The requested ref is not a valid branch, tag, or commit name.`

One spelling of one rule is shadowed by the argument parser. Space-separated `--ref -something` never reaches validation: it fails with exit `1` and `error: Option 'ref' is defined but no value has been provided.` The same value reaches `invalid_ref` (exit `2`) when written `--ref=-something` or as the embedded `owner/repo@-something`. Whitespace is the reverse: `owner/repo@a b` is rejected as `invalid_source`, because the `owner/repo` pattern's ref group cannot contain whitespace, so only `--ref "a b"` reaches `invalid_ref`.

`dotnet-git-tool` clones with `--depth 1`, so a bare commit SHA that is unreachable in a depth-1 fetch fails, while branch names and tag names work. See the [repository cache reference](repository-cache.md) for the fetch and checkout sequence.

---

## dotnet git-tool install

```text
dotnet git-tool install <REPOSITORY> [OPTIONS]
```

Clones or refreshes the cached repository, discovers a project, packs it into a generated package, installs that package as a .NET global tool, and writes an **installation record**. The source ID must not already be managed.

| Argument | Description |
|---|---|
| `<REPOSITORY>` | GitHub owner/repo with an optional @ref, or a Git repository URL. |

| Option | Description |
|---|---|
| `--ref <REF>` | Branch, tag, or commit to install; overrides an embedded @ref. |
| `-p, --project <PATH>` | Project file or directory inside the repository. |
| `--standalone` | Expose an unprefixed command, such as 'bookmeta'. |
| `--dotnet-command` | Expose a .NET subcommand, such as 'dotnet bookmeta' (the install default). |

Plus the [mutation options](#mutation-options) and the [global options](#global-options).

With neither style flag, `install` uses dotnet **command style**. Both flags together fail with `invalid_command_style` (exit `2`) and the message `--standalone and --dotnet-command cannot be used together.` The command style is part of the generated version (`0.0.0-git.4fbe47e66359.dotnet` against `0.0.0-git.4fbe47e66359.standalone`), so choosing a style also chooses a version string; see [how it works](how-it-works.md).

`--project` overrides the repository manifest's `project` field only. The manifest's `command` field still applies. See [authoring tools](authoring-tools.md) for the manifest and the discovery order.

### Examples

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
dotnet git-tool install JKamsker/bookmeta-cli --yes
dotnet git-tool install JKamsker/bookmeta-cli --yes --standalone
dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0 --yes --project src/BookMeta.Cli/BookMeta.Cli.csproj
```

### Output

Preview on a cache that holds no copy of the repository yet:

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
```

Output:

```text
Would prepare cached sources for JKamsker/bookmeta-cli, discover a tool project, pack it for a 'dotnet <command>' invocation, install it globally, and retain clean sources at /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac.
```

With `--project`, the phrase `a tool project` is replaced by the value you passed. The same preview as a JSON envelope:

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run --json
```

Output:

```json
{
  "ok": true,
  "data": {
    "action": "install",
    "source": "JKamsker/bookmeta-cli",
    "project": null,
    "commandStyle": "dotnet",
    "repositoryPath": "/home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac",
    "repositoryCached": false,
    "executesRepositoryCode": true
  },
  "error": null,
  "meta": {
    "schemaVersion": 1,
    "warnings": []
  }
}
```

Without `--dry-run` and without `--yes`, an interactive terminal receives a warning line on stderr followed by an unterminated prompt that waits for your answer:

```text
Warning: building 'JKamsker/bookmeta-cli' can execute arbitrary code from that repository.
Continue? [y/N] 
```

A successful install writes progress lines to stderr and this message to stdout:

```text
Installed JKamsker/bookmeta-cli at 4fbe47e66359. Command: dotnet bookmeta. Clean sources: /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

Under `--json`, a successful install carries `"action": "installed"` and an `installation` object holding the new [installation record](#installation-record-keys).

### Errors

`install` can raise `already_installed` (exit `6`); `confirmation_required`, `invalid_source`, `invalid_ref`, and `invalid_command_style` (exit `2`); every project-discovery kind and every repository-cache kind; `state_locked` (exit `6`); `invalid_state`, `child_process_failed`, and `unexpected_error` (exit `1`); `dependency_not_found` (exit `5`); and `cancelled` (exit `10`). See [error kinds](#error-kinds) for each meaning.

## dotnet git-tool update

```text
dotnet git-tool update <REPOSITORY> [OPTIONS]
```

Refreshes the cached repository for a managed source ID, rebuilds it, and updates the installed global tool. The generated package ID is reused from the installation record.

| Argument | Description |
|---|---|
| `<REPOSITORY>` | Previously installed owner/repo, optionally with a new @ref. |

| Option | Description |
|---|---|
| `--ref <REF>` | Branch, tag, or commit to update to; omit to use the remote default branch. |
| `-p, --project <PATH>` | Override the recorded project file or directory. |
| `--standalone` | Expose an unprefixed command, such as 'bookmeta'. |
| `--dotnet-command` | Expose a .NET subcommand, such as 'dotnet bookmeta' (the install default). |

Plus the [mutation options](#mutation-options) and the [global options](#global-options).

`update` uses the remote default branch when you omit a ref. Other omitted values come from the installation
record:

| Value | Source when you omit the option |
|---|---|
| Requested ref | None. The remote default branch is selected and the recorded `requestedRef` becomes null. |
| Project | The recorded `project`. |
| Clone URL | The recorded `cloneUrl`. Passing a different URL that normalizes to the same source ID does not repoint the remote. |
| Command style | The recorded `commandStyle`. A record without `commandStyle` is inferred as standalone style unless its `command` string starts with `dotnet ` or `dotnet-`; pass `--dotnet-command` to correct a misinferred record. |

### Examples

```console
dotnet git-tool update JKamsker/bookmeta-cli --dry-run
dotnet git-tool update JKamsker/bookmeta-cli --yes
dotnet git-tool update JKamsker/bookmeta-cli --ref v1.2.0 --yes
dotnet git-tool update JKamsker/bookmeta-cli --yes --standalone
```

### Output

```console
dotnet git-tool update JKamsker/bookmeta-cli --dry-run
```

Output:

```text
Would refresh cached sources for JKamsker/bookmeta-cli, rebuild src/BookMeta.Cli/BookMeta.Cli.csproj for a 'dotnet <command>' invocation, update git.JKamsker.bookmeta-cli globally, and retain clean sources at /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac.
```

The `--dry-run --json` `data` object matches the `install` preview except that `action` is `update` and `project` carries the recorded value.

A real update takes one of two paths. When the resolved commit changed, or the command style changed, the JSON `action` is `updated` and the human message is:

```text
Updated JKamsker/bookmeta-cli to 4fbe47e66359. Command: dotnet bookmeta. Clean sources: /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

When both the resolved commit and the command style match the record, the `action` is `unchanged` and the message is:

```text
JKamsker/bookmeta-cli is already at 4fbe47e66359. Clean sources: /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

An `unchanged` result still rewrites the installation record, reconciling `commandStyle` and `repositoryPath`, which repairs a null or stale `repositoryPath`. It deliberately leaves `updatedAt` untouched, so `updatedAt` tracks real package changes rather than update attempts, and `cache show` can report `Updated -` on a repository you have updated several times. Changing the command style alone forces a full rebuild at the same commit, because the style is part of the generated version.

### Errors

`update` raises the same kinds as `install`, minus `already_installed`. An unmanaged source ID fails with `installation_not_found` (exit `5`) and the message `'<SOURCE_ID>' is not managed. Install it first with 'dotnet git-tool install <REPOSITORY_AS_TYPED>'.` The first value is the normalized source ID and the second echoes the repository argument exactly as you typed it, so `update https://github.com/JKamsker/bookmeta-cli` reports `'JKamsker/bookmeta-cli' is not managed. Install it first with 'dotnet git-tool install https://github.com/JKamsker/bookmeta-cli'.`

## dotnet git-tool uninstall

```text
dotnet git-tool uninstall <REPOSITORY> [OPTIONS]
```

Runs `dotnet tool uninstall --global` for the generated package and removes the installation record. The **cached repository** is retained; use [`cache prune`](#dotnet-git-tool-cache-prune) to reclaim that disk space.

| Argument | Description |
|---|---|
| `<REPOSITORY>` | Previously installed owner/repo or repository URL. |

Options are the [mutation options](#mutation-options) and the [global options](#global-options). There is no `--ref`, no `--project`, and no command-style flag.

### Examples

```console
dotnet git-tool uninstall JKamsker/bookmeta-cli --dry-run
dotnet git-tool uninstall JKamsker/bookmeta-cli --yes
```

### Output

```console
dotnet git-tool uninstall JKamsker/bookmeta-cli --dry-run
```

Output:

```text
Would uninstall git.JKamsker.bookmeta-cli and remove the record for JKamsker/bookmeta-cli.
```

The `--dry-run --json` `data` object is `"action": "uninstall"` plus the `installation` object. On success the action becomes `"uninstalled"` and the object still carries the record that was removed. Without `--yes`, an interactive terminal receives an unterminated prompt on stderr:

```text
Uninstall 'JKamsker/bookmeta-cli'? [y/N] 
```

The success message is:

```text
Uninstalled JKamsker/bookmeta-cli (git.JKamsker.bookmeta-cli). Cached sources retained at /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac.
```

The second sentence is omitted when the record has no `repositoryPath`.

### Errors

`uninstall` can raise `installation_not_found` (exit `5`), whose message is `'<SOURCE_ID>' is not managed by dotnet git-tool.`; `confirmation_required` (exit `2`); `child_process_failed`, `invalid_state`, and `unexpected_error` (exit `1`); `dependency_not_found` (exit `5`) when `dotnet` is missing from `PATH`; `state_locked` (exit `6`); and `cancelled` (exit `10`). See [error kinds](#error-kinds) for each meaning.

## dotnet git-tool list

```text
dotnet git-tool list [OPTIONS]
```

Prints every installation record. Takes only the [global options](#global-options). `--quiet` does not suppress its output.

### Examples

```console
dotnet git-tool list
dotnet git-tool list --json
```

### Output

Human output is a five-column, space-padded table sorted by source ID, case-insensitively:

```text
SOURCE                         PACKAGE                            COMMIT         COMMAND                  CACHE PATH
JKamsker/bookmeta-cli          git.JKamsker.bookmeta-cli          4fbe47e663597… dotnet bookmeta          /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

| Column | Width | Truncation |
|---|---|---|
| `SOURCE` | 30 | Truncated to 29 characters plus `…` |
| `PACKAGE` | 34 | Truncated to 33 characters plus `…` |
| `COMMIT` | 14 | Truncated to 13 characters plus `…`, so a 40-character SHA always truncates |
| `COMMAND` | 24 | Padded, never truncated |
| `CACHE PATH` | none | Never truncated |

`COMMAND` and `CACHE PATH` render as `-` when the record's value is null. Human output is not parseable as JSON. With no records:

```text
No source tools are installed.
```

Under `--json`, `data.installations` is an array of [installation records](#installation-record-keys), empty when nothing is managed.

### Errors

`list` reads the installation state file without taking the state lock, so it raises `invalid_state` (exit `1`) or `unexpected_error` (exit `1`). It also carries a `cancelled` branch (exit `10`) that no command line reaches. See [error kinds](#error-kinds) for each meaning.

## dotnet git-tool cache list

```text
dotnet git-tool cache list [OPTIONS]
```

Lists every direct child directory of `<CACHE_ROOT>/repositories`, managed or not. Takes only the [global options](#global-options). `--quiet` does not suppress its output. It runs several `git` invocations per cached repository, so `git` must be on `PATH`.

### Examples

```console
dotnet git-tool cache list
dotnet git-tool cache list --json
```

### Output

Human output is a four-column Spectre.Console table with rounded borders that expands to the terminal width. For that table, the meaning of every column, and the date rendering, see the [repository cache reference](repository-cache.md).

Under `--json`, `data` holds `repositoryRoot` (the absolute `<CACHE_ROOT>/repositories` path) and `repositories`, an array of [cached repository objects](#cached-repository-keys). `sizeBytes` is `null` there; only `cache show` computes it.

### Errors

`cache list` can raise `invalid_state` (exit `1`), `dependency_not_found` (exit `5`) when `git` is missing from `PATH`, and `unexpected_error` (exit `1`). It reads the installation state file without taking the state lock. See [error kinds](#error-kinds) for each meaning.

## dotnet git-tool cache show

```text
dotnet git-tool cache show <REPOSITORY> [OPTIONS]
```

Prints Git, package, state, size, and path detail for one cached repository. The argument here is a selector, not a repository specification. Takes only the [global options](#global-options). `--quiet` does not suppress its output.

| Argument | Description |
|---|---|
| `<REPOSITORY>` | Source ID, repository name, package ID, or cache directory name. |

An exact, unique, case-insensitive source ID match wins outright. Otherwise the selector is matched against the repository name, the cache directory name, and the record's package ID. For the human output of this command, the meaning of every field it prints, and the full resolution rules, see the [repository cache reference](repository-cache.md).

### Examples

```console
dotnet git-tool cache show JKamsker/bookmeta-cli
dotnet git-tool cache show bookmeta-cli
dotnet git-tool cache show git.JKamsker.bookmeta-cli
dotnet git-tool cache show JKamsker-bookmeta-cli-1cd22d4b86ac --json
```

### Output

Under `--json`, `data.repository` is one [cached repository object](#cached-repository-keys) with `sizeBytes` populated.

### Errors

`cache show` can raise `cache_repository_not_found` (exit `5`), whose message is `Cached repository '<SELECTOR>' was not found.`; `ambiguous_cache_repository` (exit `2`), whose message lists the candidate source IDs; `invalid_state` and `unexpected_error` (exit `1`); and `dependency_not_found` (exit `5`) when `git` is missing from `PATH`. It reads the installation state file without taking the state lock. See [error kinds](#error-kinds) for each meaning.

A blank selector never reaches the command body: `dotnet git-tool cache show ""` exits `1` with the plain-text line `error: A repository name is required.` and produces no JSON envelope, even with `--json`.

## dotnet git-tool cache prune

```text
dotnet git-tool cache prune [OPTIONS]
```

Deletes every direct child of `<CACHE_ROOT>/repositories` that no installation record uses. Takes the [mutation options](#mutation-options) and the [global options](#global-options).

A cached repository counts as used when it is any record's `repositoryPath`, or when it is the deterministic directory recomputed from any record's source ID. A cached repository whose repository lock is held by another operation is skipped and reported, not treated as an error. See the [repository cache reference](repository-cache.md) for the containment rules.

### Examples

```console
dotnet git-tool cache prune --dry-run
dotnet git-tool cache prune --dry-run --json
dotnet git-tool cache prune --yes
```

### Output

```console
dotnet git-tool cache prune --dry-run
```

Output:

```text
Would remove 1 unused cached repository:
  /home/you/.cache/dotnet-git-tool/repositories/orphan-abc123def456
```

With nothing to remove, the preview prints this instead:

```text
No unused repositories found in /home/you/.cache/dotnet-git-tool/repositories.
```

An executed prune reports one of:

```text
Removed <N> unused cached repositories.
Removed <N> unused cached repositories. Skipped <M> unused cached repositories currently in use.
```

The noun is `repository` when the count is exactly one and `repositories` otherwise, applied independently to the preview count, the removed count, and the skipped count. The interactive prompt goes to stderr, is unterminated, and always uses the plural form:

```text
Remove 3 unused cached repositories from '/home/you/.cache/dotnet-git-tool/repositories'? [y/N] 
```

The `data` objects are:

| Mode | `action` | Additional keys |
|---|---|---|
| `--dry-run` | `cache_prune_preview` | `repositoryRoot`, `unusedRepositoryPaths` |
| executed | `cache_pruned` | `repositoryRoot`, `removedRepositoryPaths`, `skippedInUseRepositoryPaths` |

Both path arrays hold absolute cache directories.

### Confirmation exception

`cache prune` skips the confirmation entirely when the plan is empty. A scheduled `dotnet git-tool cache prune --json` on a clean cache exits `0` with `"action": "cache_pruned"` and two empty arrays, without `--yes`. `confirmation_required` is returned only when the command would actually delete something.

### Errors

`cache prune` can raise `confirmation_required` and `invalid_cache_prune_path` (exit `2`), `cache_prune_failed`, `invalid_state`, and `unexpected_error` (exit `1`), and `cancelled` (exit `10`). It reads the installation state file without taking the state lock, and it starts no `git` or `dotnet` process. See [error kinds](#error-kinds) for each meaning.

---

## Exit codes

Only six values are ever returned. Codes `3`, `4`, `7`, `8`, and `9` are never produced, so do not treat the set as dense.

| Exit code | Meaning | Typical cause |
|---|---|---|
| 0 | Success | The command completed, including a `--dry-run` preview and an empty `cache prune`. |
| 1 | General error | A `git` or `dotnet` invocation failed, the installation state file is unreadable, or the argument parser rejected the command line. |
| 2 | Usage or consent | An invalid repository argument, an invalid ref, both style flags, an ambiguous project, or a required confirmation in a non-interactive session. |
| 5 | Not found | The source ID is not managed, no project was found, no cached repository matched, or `git` or `dotnet` is missing from `PATH`. |
| 6 | Conflict | The source ID is already managed, the cache directory belongs to another remote, or a lock timed out. |
| 10 | Canceled | A confirmation prompt answered with anything other than `y` or `yes`. |

Three behaviors that surprise script authors:

- Bare `dotnet git-tool` with no arguments prints the root help screen on stdout and exits `0`.
- An unknown command exits `1`, not 2: `dotnet git-tool bogus` writes `error: Unknown command 'bogus'.` to stderr.
- Nothing in `dotnet-git-tool` handles Ctrl-C. No cancellation handler is registered anywhere in the source, so an interrupt is left to the runtime and the operating system and does not produce exit `10`. Do not branch on `10` to detect an interrupt.

Every argument-parsing and validation failure exits `1` with a plain `error:` line on stderr, never `2`. Exit `2` comes only from errors `dotnet-git-tool` raises after a command starts running.

## Error kinds

Every failure that reaches a command body carries an **error kind**, exposed as `error.kind` in the JSON envelope.

| Kind | Exit code | Meaning |
|---|---|---|
| `child_process_failed` | 1 | Any non-zero `git` or `dotnet` exit: clone, fetch, checkout, reset, clean, project evaluation, pack, or `dotnet tool install`, `update`, `uninstall`. |
| `invalid_source` | 2 | The repository argument is neither an `owner/repo` value nor a supported URL. |
| `invalid_ref` | 2 | The requested ref failed validation. |
| `invalid_command_style` | 2 | `--standalone` and `--dotnet-command` were both passed. |
| `confirmation_required` | 2 | Confirmation was required and the session is non-interactive. |
| `already_installed` | 6 | The source ID already has an installation record. |
| `installation_not_found` | 5 | `update` or `uninstall` found no record for the source ID. |
| `project_not_found` | 5 | No `.csproj`, no executable project, or the `--project` path does not exist. |
| `ambiguous_project` | 2 | Several `PackAsTool` projects, or several executable projects. The message lists the paths and suggests `--project <PATH>`. |
| `project_not_executable` | 2 | The explicitly selected project is neither `OutputType=Exe` nor `PackAsTool=true`. |
| `invalid_project` | 2 | The selected project is outside the cached repository, or is a directory that does not hold exactly one `.csproj`. |
| `invalid_manifest` | 2 | `.config/dotnet-git-tool.json` could not be parsed. |
| `project_evaluation_failed` | 1 | MSBuild returned an unreadable project evaluation. |
| `invalid_tool_command` | 2 | The discovered command name does not match `^[A-Za-z0-9][A-Za-z0-9_.-]*$`. |
| `invalid_repository_cache` | 6 | The cache directory exists but holds no `.git`. Nothing is deleted. |
| `repository_cache_conflict` | 6 | The cache directory's origin does not match the requested clone URL. |
| `repository_cache_locked` | 6 | The repository lock was still held after 30 seconds. |
| `repository_cache_dirty` | 1 | The working tree was still dirty after reset and clean. |
| `default_branch_not_found` | 1 | The remote did not report a symref HEAD. |
| `state_locked` | 6 | The installation state lock was still held after 10 seconds. Only `install`, `update`, and `uninstall` take that lock. |
| `invalid_state` | 1 | `installed.json` is unreadable or its `schemaVersion` is not `1`. |
| `cache_repository_not_found` | 5 | The `cache show` selector matched nothing. |
| `ambiguous_cache_repository` | 2 | The `cache show` selector matched several cached repositories. |
| `cache_prune_failed` | 1 | A cached repository could not be deleted. |
| `invalid_cache_prune_path` | 2 | A prune target is not a direct child of `<CACHE_ROOT>/repositories`. |
| `dependency_not_found` | 5 | `Could not start '<COMMAND>'. Make sure it is installed and available on PATH.` |
| `cancelled` | 10 | A confirmation prompt answered with anything other than `y` or `yes`, matched case-insensitively, including end of input. |
| `unexpected_error` | 1 | Any other exception, carrying the runtime's message. |

`invalid_state` reaches almost every command, the whole `cache` branch included, because they all read the installation state file. The exception is the `cache` branch when `<CACHE_ROOT>/repositories` does not exist yet: all three commands return before reading the file, so `cache list` and `cache prune` exit `0` even with an unreadable `installed.json`, and `cache show` fails with `cache_repository_not_found` (exit `5`) instead.

One kind is unreachable from the command line. `invalid_cache_repository` (exit `2`) exists in the source, but the argument validator rejects a blank `cache show` selector first. One route into `cancelled` is unreachable too: `list`, `cache list`, `cache show`, `cache prune`, and the install pipeline shared by `install`, `update`, and `uninstall` each map a canceled operation onto `cancelled` (exit `10`), and nothing ever signals that cancellation. The confirmation prompt is the only thing that produces exit `10`.

A `child_process_failed` message takes one of two shapes:

```text
<OPERATION> failed: <LAST_OUTPUT_LINE>
<OPERATION> failed with exit code <N>.
```

`<LAST_OUTPUT_LINE>` is the last non-empty line of the child's stderr, not the first, falling back to the last non-empty line of its stdout. Child output is buffered and never streamed, and `--verbose` does not change that, so a failing build collapses to a single line. To see the full log, reproduce the build yourself inside the cached repository.

`<OPERATION>` names the stage that failed:

| Stage | `<OPERATION>` values |
|---|---|
| Clone, fetch, and checkout | `Cloning <SOURCE_ID>`, `Fetching ref <REF>`, `Checking out ref <REF>`, `Fetching origin/<BRANCH>`, `Checking out origin/<BRANCH>` |
| Remote and commit resolution | `Refreshing the cached repository origin`, `Resolving the remote default branch`, `Validating the cached repository origin`, `Resolving the cached commit` |
| Reset and clean | `Resetting the cached repository`, `Resetting cached submodules`, `Cleaning build artifacts from the cached repository`, `Verifying the cached repository` |
| Project evaluation | `Evaluating <PROJECT>` |
| Packing | `Packing <PROJECT>` |
| Global tool commands | `Installing <PACKAGE_ID>`, `Updating <PACKAGE_ID>`, `Uninstalling <PACKAGE_ID>` |

For symptom-to-fix guidance on each kind, see the [troubleshooting guide](troubleshooting.md).

## Streams

| Content | Stream | Modes |
|---|---|---|
| The JSON envelope, success and failure alike | stdout | `--json` |
| Human results: the `list` table, the `cache list` table, `cache show` detail, `--dry-run` previews, success messages | stdout | human |
| Progress status lines | stderr | human, not under `--quiet` |
| `--verbose` diagnostic lines | stderr | human, not under `--quiet` |
| The confirmation prompt | stderr | human, interactive only |
| `error: <MESSAGE>` failure lines | stderr | human |

Under `--json`, the tool writes nothing to stderr once a command body starts running, on success and on failure alike. Stderr is not empty for parse-time failures, so branch on the exit code and on whether stdout parsed as JSON rather than on stderr being empty.

Parse-time failures are the exception. An argument-parsing or validation error is handled globally: it writes a plain `error: <MESSAGE>` line to stderr, leaves stdout empty, and exits `1`, even with `--json`. A JSON consumer must handle "empty stdout plus a non-zero exit" as a valid outcome.

Neither `git` nor MSBuild output is ever streamed, so a long clone or a multi-minute build prints nothing. Because stdin is inherited rather than redirected, a Git credential helper that wants to prompt makes the process appear to hang with no output.

## The JSON envelope

Every command that reaches its body under `--json` writes exactly one envelope to stdout. It is indented and camelCase, serialized with `System.Text.Json` web defaults, which escape the apostrophe as `\u0027` inside messages. Leave that escape as-is when you compare message text.

```console
dotnet git-tool install JKamsker/bookmeta-cli --json
```

Output:

```json
{
  "ok": false,
  "data": null,
  "error": {
    "kind": "confirmation_required",
    "message": "Building \u0027JKamsker/bookmeta-cli\u0027 can execute arbitrary repository code. Inspect with --dry-run or explicitly consent with --yes."
  },
  "meta": {
    "schemaVersion": 1,
    "warnings": []
  }
}
```

| Key | Type | Meaning |
|---|---|---|
| `ok` | boolean | `true` when the command succeeded. |
| `data` | object or null | The `data` object for the command. Null on failure. |
| `error` | object or null | Null on success. Otherwise `kind` and `message`, both strings. |
| `meta.schemaVersion` | integer | Always `1` today. |
| `meta.warnings` | array of string | Always empty today. |

### The data object by command

| Command | `data` keys |
|---|---|
| `install --dry-run` | `action` (`install`), `source`, `project`, `commandStyle`, `repositoryPath`, `repositoryCached`, `executesRepositoryCode` |
| `install` | `action` (`installed`), `installation` |
| `update --dry-run` | `action` (`update`), `source`, `project`, `commandStyle`, `repositoryPath`, `repositoryCached`, `executesRepositoryCode` |
| `update` | `action` (`updated` or `unchanged`), `installation` |
| `uninstall --dry-run` | `action` (`uninstall`), `installation` |
| `uninstall` | `action` (`uninstalled`), `installation` |
| `list` | `installations` |
| `cache list` | `repositoryRoot`, `repositories` |
| `cache show` | `repository` |
| `cache prune --dry-run` | `action` (`cache_prune_preview`), `repositoryRoot`, `unusedRepositoryPaths` |
| `cache prune` | `action` (`cache_pruned`), `repositoryRoot`, `removedRepositoryPaths`, `skippedInUseRepositoryPaths` |

In the preview `data` objects, `source` carries the requested ref when one is set (`JKamsker/bookmeta-cli@v1.2.0`), `commandStyle` is `dotnet` or `standalone`, `repositoryCached` reports whether `<CACHE_DIRECTORY>/.git` already exists, and `executesRepositoryCode` is always `true`.

### Installation record keys

Used by `list`, by the `installation` object in `install`, `update`, and `uninstall`, and by the `installation` object nested in a cached repository.

| Key | Type | Meaning |
|---|---|---|
| `sourceId` | string | The normalized identity, for example `JKamsker/bookmeta-cli`. |
| `cloneUrl` | string | The URL `git clone` was given. |
| `requestedRef` | string or null | The pinned branch, tag, or commit. Null means the remote default branch. |
| `project` | string | The selected project, relative to the repository root. |
| `packageId` | string | The generated package ID. |
| `version` | string | The generated version, for example `0.0.0-git.4fbe47e66359.dotnet`. |
| `commit` | string | The full commit SHA that was built, exactly as `git rev-parse HEAD` reported it. |
| `command` | string or null | The invocation you type, for example `dotnet bookmeta`. |
| `commandStyle` | string or null | `dotnet` or `standalone`. Absent in records written by older builds. |
| `repositoryPath` | string or null | The cache directory of the cached repository. |
| `installedAt` | string | An ISO 8601 timestamp with offset. |
| `updatedAt` | string or null | Null until a real package change occurs. Optional in the on-disk file. |

### Cached repository keys

Used by `cache list` and `cache show`.

| Key | Type | Meaning |
|---|---|---|
| `sourceId` | string | The source ID, or for an unmanaged entry the origin-derived value, falling back to the cache directory name. |
| `repositoryName` | string | The repository half of the source ID. |
| `path` | string | The absolute cache directory. |
| `origin` | string or null | The output of `git remote get-url origin`. |
| `branch` | string or null | The current branch. Null when HEAD is detached or the directory is not a Git repository. |
| `commit` | string or null | The full HEAD commit. |
| `revision` | string or null | The output of `git describe --tags --always --dirty`. |
| `commitDate` | string or null | The HEAD commit date, ISO 8601 with offset. |
| `isGitRepository` | boolean | `false` when `git rev-parse HEAD` fails for the directory, which covers a plain directory in the cache root and a repository with no commits. Such a directory is still listed, still inspectable, and still prunable. |
| `isDirty` | boolean or null | Null when the state cannot be determined. |
| `sizeBytes` | integer or null | Always null in `cache list`. Computed on demand by `cache show`. |
| `installation` | object or null | The installation record, or null when unmanaged. |
| `sourceVersion` | string or null | The version read out of the checked-out project file or an ancestor `Directory.Build.props`. Populated only for a managed cached repository. |
| `isManaged` | boolean | `true` when an installation record matched. |

For `jq` recipes, exit-code branching, and a GitHub Actions example, see the [automation guide](automation.md).

---

## See also

- [Documentation index](README.md)
- [Getting started](getting-started.md)
- [Security guide](security.md)
- [Repository cache reference](repository-cache.md)
- [Troubleshooting guide](troubleshooting.md)
