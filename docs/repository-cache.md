# Repository cache

`dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`) builds every tool from a real Git working tree on your disk. **The repository cache** is where those working trees live, and it keeps them after the build finishes.

Paths in the examples on this page use the Linux defaults described in [Where the cache lives](#where-the-cache-lives). Commit hashes, versions, sizes, and dates are illustrative and differ on your machine.

Three commands read and write the cache directly. Each is documented in full further down.

| Command | What it does |
|---|---|
| [`dotnet git-tool cache list`](#dotnet-git-tool-cache-list) | Every cached repository, one row each. |
| [`dotnet git-tool cache show`](#dotnet-git-tool-cache-show) | Everything known about one of them, including its path and size. |
| [`dotnet git-tool cache prune`](#dotnet-git-tool-cache-prune) | Delete cached repositories that no [source tool](README.md#glossary) needs. |

## Why the cache exists

`install` and `update` run MSBuild against a checked-out repository, so the sources have to exist as files. Keeping them buys three things:

- **Reuse.** `update` reuses the same **cache directory** for a given [source ID](README.md#glossary), the normalized identity of the repository such as `JKamsker/bookmeta-cli`. It fetches the new commit and checks it out instead of cloning again, and an `install` that follows an `uninstall` does the same. A repeat `install` of a source ID that is still managed never reaches the cache: it fails with `already_installed` (exit `6`) before the repository lock is taken.
- **Inspection.** `uninstall` does not delete the sources. Read exactly what was built at the path that `cache show` prints. See [Security](security.md).
- **Predictability.** Every source ID maps to one directory under the current cache root, computed from the source ID alone, so the same repository lands in the same place on every run. Two repositories whose URLs differ only above their last two path segments share one source ID and therefore contend for one cache directory; see [the repository argument](cli-reference.md#the-repository-argument).

The cache holds sources, never build output. `dotnet pack` writes its package into a directory under the system temp directory (`dotnet-git-tool-package-<GUID>`), and `dotnet-git-tool` deletes that directory when the install step finishes. Deletion is best effort: a file another process holds open can leave the temp directory behind, with no error reported. The `bin/` and `obj/` folders that MSBuild creates inside the cache directory during the build are removed by [the clean guarantee](#the-clean-guarantee) before the command returns.

## Where the cache lives

The **cache root** defaults to `~/.cache/dotnet-git-tool` on Linux and macOS, which is `/home/you/.cache/dotnet-git-tool` in the examples on this page, and to `C:\Users\You\AppData\Local\dotnet-git-tool\cache` on Windows. macOS uses the same location as Linux; it does not use `~/Library/Caches`. `DOTNET_GIT_TOOL_CACHE` and `XDG_CACHE_HOME` both move it.

The cache root and the **state directory** that holds `installed.json` are resolved independently. Only on Windows does the cache root sit inside the state directory; on Linux and macOS they have different parents. For the full resolution order, the per-platform defaults, the state paths, and why a relative value makes the resolved location depend on where you ran the command, see [Configuration](configuration.md#cache-root-resolution).

## On-disk layout

`dotnet-git-tool` creates two directories in the cache root, both on demand:

```text
/home/you/.cache/dotnet-git-tool
├── repositories
│   ├── JKamsker-bookmeta-cli-1cd22d4b86ac
│   └── orphan-abc123def456
└── locks
    ├── JKamsker-bookmeta-cli-1cd22d4b86ac.lock
    └── orphan-abc123def456.lock
```

`repositories/` holds one **cached repository** per directory. `orphan-abc123def456` deliberately does not follow the naming scheme below. It is **unmanaged**: no installation record points at it, and `dotnet-git-tool` did not create it. The cache accepts it anyway.

`locks/` holds one zero-byte **repository lock** file per cache directory name, described in [Concurrency](#concurrency). Lock files are never deleted, and `cache prune` creates them too, so `locks/` grows by one file per cache directory name ever used and stays that way. `cache prune` does not clean it, because it only ever deletes direct children of `repositories/`. A lock file existing does not mean anything is locked.

`dotnet-git-tool` never removes or rejects anything else it finds in the cache root. Pointing `DOTNET_GIT_TOOL_CACHE` at a directory that already holds unrelated content is therefore harmless: `cache list` and `cache prune` read only `repositories/`.

### Cache directory names

A cache directory name is `<SANITIZED_SOURCE_ID>-<HASH_PREFIX>`, built from the source ID alone:

1. Every run of characters outside `A-Za-z0-9_.-` becomes a single `-`.
2. Leading and trailing `-` characters are trimmed.
3. The first 12 hexadecimal characters of the SHA-256 hash of the source ID, lowercased, are appended.

For the source ID `JKamsker/bookmeta-cli`, step 1 turns the `/` into `-`, producing `JKamsker-bookmeta-cli`. The hash suffix is `1cd22d4b86ac`, so the directory is `JKamsker-bookmeta-cli-1cd22d4b86ac`. You can reproduce the suffix yourself.

Linux and macOS:

```bash
printf '%s' 'JKamsker/bookmeta-cli' | shasum -a 256 | cut -c1-12
```

On a Linux machine without `shasum`, `sha256sum` from GNU coreutils takes the same pipeline.

Windows (PowerShell):

```powershell
$bytes = [System.Text.Encoding]::UTF8.GetBytes('JKamsker/bookmeta-cli')
(Get-FileHash -InputStream ([System.IO.MemoryStream]::new($bytes)) -Algorithm SHA256).Hash.Substring(0, 12).ToLower()
```

The hash keeps two different source IDs apart even when they sanitize to the same name. It also means the directory is derived, not stored: renaming a cache directory does not move the cached repository with it. The renamed directory becomes unmanaged and is a `cache prune` candidate, and the next `install` or `update` clones the canonical directory again.

Directories that do not follow this scheme are still first-class members of the cache. Any direct child of `repositories/` is listed by `cache list`, resolvable by `cache show`, and eligible for `cache prune`, whether or not it is a Git repository at all.

## Lifecycle of a cached repository

### First clone

With no directory at the computed path, `dotnet-git-tool` takes the repository lock and runs:

```console
git clone --depth 1 --no-tags https://github.com/JKamsker/bookmeta-cli.git /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

If you supplied a **requested ref**, `JKamsker/bookmeta-cli@v1.2.0` for example, it then fetches and checks it out detached:

```console
git fetch --depth 1 origin v1.2.0
git checkout --detach FETCH_HEAD
```

Finally `git rev-parse HEAD` records the commit that the package version is derived from. There is no clean step on this path, because a newly cloned directory is clean by construction. If `git clone` fails and the directory did not exist beforehand, the incomplete cache directory is deleted.

`git clone` runs shallow and fetches no tags. Two consequences are worth knowing. A branch or tag name works as a requested ref, because the ref is fetched by name; a raw commit SHA works only if the remote allows fetching an arbitrary SHA. And a build that derives its own version from Git history sees a single commit and no tags.

Submodules are never initialized. `git clone` does not pass `--recurse-submodules` and nothing runs `git submodule update`, so a repository that needs submodule content to build fails with `child_process_failed` (exit `1`).

### Reuse and validation

When the directory already exists, it is validated before anything else happens. Both checks refuse to continue and neither deletes anything:

| Check | Failure | Message |
|---|---|---|
| `.git` exists inside the cache directory | `invalid_repository_cache`, exit `6` | `Cache path '<PATH>' exists but is not a Git repository. Move it aside and retry.` |
| `git remote get-url origin` equals the clone URL, compared case-insensitively | `repository_cache_conflict`, exit `6` | `Cache path '<PATH>' belongs to a different remote ('<ORIGIN>'). Move it aside and retry.` |

The remedy in both messages is literal. Move the directory out of the cache root, or rename it and move it somewhere outside `repositories/`, or point `DOTNET_GIT_TOOL_CACHE` at a different cache root, then run the command again. Do not leave the renamed directory inside `repositories/`: anything there that no installation record protects is a `cache prune` target.

A directory you put at the cache path therefore survives the error intact. That protection is specific to `install` and `update`. `cache prune` does delete any direct child of `repositories/` that no installation record protects, including one you created by hand. See [`dotnet git-tool cache prune`](#dotnet-git-tool-cache-prune).

### Synchronization on update

After validation, the cached repository is cleaned, and then the origin URL is refreshed with `git remote set-url origin https://github.com/JKamsker/bookmeta-cli.git`.

With a requested ref, the same fetch-and-detach pair as the first clone runs, and `cache show` afterwards reports `detached` in its `Branch` field, because the checkout is detached at `FETCH_HEAD`.

Without a requested ref, the default branch is resolved from the remote with `git ls-remote --symref origin HEAD`. A remote that reports no symref produces `default_branch_not_found` (exit `1`). Otherwise, for a repository whose default branch is `main`:

```console
git fetch --depth 1 origin +refs/heads/main:refs/remotes/origin/main
git checkout -B main refs/remotes/origin/main
```

Both paths move the working tree onto the fetched commit with a checkout, never with a merge and never with a rebase. No merge commit is ever created in a cached repository, and nothing you edit inside one survives the next run.

## The clean guarantee

Every clean runs the same four steps in order:

1. `git reset --hard HEAD`
2. `git submodule foreach --recursive "git reset --hard HEAD && git clean -ffdx"`
3. `git clean -ffdx`
4. `git status --porcelain --untracked-files=all`

Step 4 is the guarantee. If it prints any non-whitespace output, the command fails with `repository_cache_dirty` (exit `1`) and the message `Repository cache '<PATH>' is still dirty after cleanup.` That failure means the reset and clean both ran and something survived them, which in practice means a file another process is holding open or a file the current user cannot delete. Close whatever holds the file, or move the cache directory aside.

The sequence runs at three points:

- **Before the build**, every time an existing cached repository is reused. A first clone skips this, since there is nothing to reset.
- **After a successful pack**, before the global install step, so the sources are already clean while the tool is being installed.
- **On dispose**, when the cache handle is released. The handle is held with `await using`, so this also runs on the failure path.

`git clean -ffdx` removes ignored files as well as untracked ones. `bin/`, `obj/`, and anything matched by `.gitignore` are deleted. Do not keep files of your own inside a cache directory; they will not be there next time.

Step 2 is a no-op in normal use, because submodules are never initialized.

## Concurrency

Each cached repository is protected by its own repository lock at `<CACHE_ROOT>/locks/<DIRECTORY_NAME>.lock`, opened for exclusive access. Because the lock is per repository, two installs of two different [source tools](README.md#glossary) can run at the same time, though both briefly contend for the installation state lock described below, and both drive `dotnet tool install --global` against the same global tools store.

`install` and `update` retry the lock every 100 milliseconds for up to 30 seconds. On timeout they fail with `repository_cache_locked` (exit `6`) and the message `Timed out waiting for another operation using this repository cache.` The fix is to wait for the other operation to finish and run the command again; there is no flag that breaks the lock.

The lock is the exclusive handle a running process holds on that file, not the file's existence, so it is released when the process exits, including when it crashes. A `.lock` file left behind in `locks/` blocks nothing, and deleting one by hand achieves nothing.

`cache prune` behaves differently: it makes one non-blocking attempt per repository and skips anything it cannot lock. See [`dotnet git-tool cache prune`](#dotnet-git-tool-cache-prune).

A separate lock guards the installation state file, with its own shorter timeout and its own error kind. See [Configuration](configuration.md).

## dotnet git-tool cache list

`cache list` inspects every direct child of `repositories/`, sorted by path, and reports what it finds. Inspection runs six `git` commands per directory (`remote get-url origin`, `rev-parse HEAD`, `branch --show-current`, `describe --tags --always --dirty`, `show -s --format=%cI HEAD`, and `status --porcelain --untracked-files=all`), so `git` has to be on `PATH` and the cost scales with the number of cached repositories. Directories are inspected concurrently.

Inspection also reads the installation state file, to decide which directories are **managed**. An unreadable `installed.json` therefore fails `cache list`, `cache show`, and `cache prune` alike with `invalid_state` (exit `1`), before a single row is rendered, whenever the cache root already holds a `repositories` directory. All three return before touching the state file when that directory does not exist yet. See [Troubleshooting](troubleshooting.md#every-command-fails-with-could-not-read-state-file-invalid_state).

```console
dotnet git-tool cache list
```

Output:

```text
╭───────────────────────┬──────────────────────┬──────────────┬──────────────╮
│ Source                │ Version              │ Installed at │ Published at │
├───────────────────────┼──────────────────────┼──────────────┼──────────────┤
│ JKamsker/bookmeta-cli │ [1.2.0|4fbe47e66359] │ 12.08.2026   │ 10.08.2026   │
│ someone/orphan        │ [-|2030ec6cd633]     │ -            │ 25.08.2026   │
╰───────────────────────┴──────────────────────┴──────────────┴──────────────╯
```

The table expands to the terminal width, so column widths on your machine differ from the sample. With nothing cached it prints `No cached repositories found.` instead.

Four columns, and no more. There is no managed column, no path, and no cache root in the human output.

| Column | Meaning |
|---|---|
| Source | The source ID. For an unmanaged directory it is derived from `git remote get-url origin`, falling back to the directory name. |
| Version | `[<VERSION>\|<SHORT_COMMIT>]`, where `<VERSION>` is the project's own version, then the installed package version, then `-`, and `<SHORT_COMMIT>` is the first 12 characters of `HEAD`, or `-`. |
| Installed at | `installedAt` from the **installation record**, or `-` for an unmanaged directory. |
| Published at | The commit date of `HEAD`. |

The project's own version is read live out of the checked-out sources on every run, not from the installation record, and only for managed directories. It comes straight out of the checked-out project file, or an ancestor `Directory.Build.props` when the project file carries no version, taking `PackageVersion`, then `Version`, then `VersionPrefix`, and skipping any value that contains an MSBuild expression such as `$(VersionPrefix)`. It is `-` when none of those yields a literal value, and it is `-` for every unmanaged directory, which is why the unmanaged row shows `-` in the first half of its Version cell. For the JSON key, see [`sourceVersion` in the CLI reference](cli-reference.md#cached-repository-keys).

Both date columns render as `dd.MM.yyyy` under the invariant culture and are converted to your local time zone. The format never changes with your locale; only the calendar day can shift with the time zone.

`--json` is the only way to get paths out of this command. It returns the absolute `<CACHE_ROOT>/repositories` path under `repositoryRoot` and the complete inventory under `repositories`, with every field of every entry including the full installation record. `sizeBytes` is `null` for every entry. For the key-by-key schema, see [CLI reference](cli-reference.md).

`--quiet` does not suppress any of this output. To see the cache root your machine resolved without reading JSON, read the absolute `Path` out of `dotnet git-tool cache show <REPOSITORY>`, or run `dotnet git-tool cache prune --dry-run`, which names the `repositories/` directory when there is nothing to remove and otherwise prints the full path of every candidate.

## dotnet git-tool cache show

`cache show` takes one selector and resolves it against the same inventory `cache list` builds.

Selector resolution runs in two stages:

1. An exact, case-insensitive match on the source ID wins outright when exactly one repository matches.
2. Otherwise, a case-insensitive match on the repository name (the last segment of the source ID), the cache directory name, or the **generated package ID** of the installation record.

Zero matches produce `cache_repository_not_found` (exit `5`). More than one match produces `ambiguous_cache_repository` (exit `2`), and the message lists the candidate source IDs so you can retry with one of them. All four of these resolve the same cached repository:

```console
dotnet git-tool cache show JKamsker/bookmeta-cli
dotnet git-tool cache show bookmeta-cli
dotnet git-tool cache show JKamsker-bookmeta-cli-1cd22d4b86ac
dotnet git-tool cache show git.JKamsker.bookmeta-cli
```

Output of the first form:

```text
Source          JKamsker/bookmeta-cli
Managed         yes
Git repository  yes
Path            /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
Origin          https://github.com/JKamsker/bookmeta-cli.git
Branch          main
Commit          4fbe47e663597fb0da63f344373cfeeee99c6a26
Revision        4fbe47e
Commit date     2026-08-10 09:15:00 UTC
Working tree    clean
Size            27.26 KiB (27910 bytes)
Installed       2026-08-12 18:04:11 UTC
Updated         -
Package ID      git.JKamsker.bookmeta-cli
Package version 0.0.0-git.4fbe47e66359.dotnet
Command         dotnet bookmeta
Project         src/BookMeta.Cli/BookMeta.Cli.csproj
Requested ref   -
```

| Field | Meaning |
|---|---|
| Source | The source ID, resolved the same way as in `cache list`. |
| Managed | `yes` when an installation record points at this directory, `no` otherwise. |
| Git repository | `yes` when `git rev-parse HEAD` succeeded in the directory. |
| Path | The absolute path of the cache directory. |
| Origin | `git remote get-url origin`, or `-`. |
| Branch | The checked-out branch, `detached` when a ref was pinned, `-` when the directory is not a Git repository. |
| Commit | The full `HEAD` commit. |
| Revision | `git describe --tags --always --dirty`. |
| Commit date | The `HEAD` commit date, in UTC. |
| Working tree | `clean`, `dirty`, or `unknown` when `git status` could not be read. |
| Size | Total size of the directory, or `unknown` when it could not be walked. |
| Installed | `installedAt` from the installation record, in UTC. |
| Updated | `updatedAt` from the installation record, in UTC. |
| Package ID | The generated package ID. |
| Package version | The generated package version. |
| Command | The recorded **invocation**. |
| Project | The project path inside the repository. |
| Requested ref | The pinned ref, or `-`. |

Every field from `Installed` down is `-` for an unmanaged directory, because there is no installation record to read them from.

Two fields surprise people. `Revision` usually shows an abbreviated commit rather than a tag, because `git clone` fetches no tags and `git describe` has nothing to name. `Updated` stays `-` until an update actually changes the installed package: an update that finds the same commit and the same **command style** reports `unchanged` and deliberately leaves the timestamp alone.

`Size` is the only field that `cache list` does not compute. It is calculated on demand here by walking the whole directory tree, `.git` included, skipping directories that are symbolic links or junctions. In the JSON envelope the same value appears as `sizeBytes`, which is `null` in `cache list` and an integer here.

Dates in `cache show` are UTC. Dates in `cache list` are local. The same cached repository can therefore show two different calendar days depending on which command you run.

## dotnet git-tool cache prune

Reclaiming space is what `cache prune` is for: it removes cached repositories that no managed source tool needs.

A cache directory counts as **used** when either of two paths matches it:

- the `repositoryPath` recorded in an installation record, when that field is set, or
- the canonical path recomputed from that record's source ID.

Recomputing the canonical path is what makes the protection reliable. A record whose `repositoryPath` is `null` or points somewhere stale still protects the directory the source ID maps to. Path comparison is case-insensitive on Windows and macOS and case-sensitive on Linux.

Everything else that is a direct child of `repositories/` is unused, including directories that are not Git repositories and directories whose names do not follow the naming scheme.

Preview first:

```console
dotnet git-tool cache prune --dry-run
```

Output:

```text
Would remove 1 unused cached repository:
  /home/you/.cache/dotnet-git-tool/repositories/orphan-abc123def456
```

Then execute:

```console
dotnet git-tool cache prune --yes
```

Output:

```text
Removed 1 unused cached repository.
```

When some repositories were locked, a second sentence is appended, for example `Skipped 2 unused cached repositories currently in use.` The noun is singular when the count is 1, the same as in the messages above. When nothing is unused, the preview prints `No unused repositories found in /home/you/.cache/dotnet-git-tool/repositories.` instead.

Four behaviors matter when you automate this:

- **Path containment.** Immediately before each deletion, the candidate is re-checked to be a direct child of `<CACHE_ROOT>/repositories`. Anything else fails the command with `invalid_cache_prune_path` (exit `2`). Nothing outside the resolved cache root is ever touched.
- **Locked repositories are skipped, not failed.** Prune makes one non-blocking attempt on each repository lock; a repository held by a concurrent `install` or `update` is reported in `skippedInUseRepositoryPaths` and the command still exits `0`.
- **Read-only files do not block it.** Read-only attributes are cleared before deletion, which matters because `.git` object files are read-only on Windows. A cache directory that is itself a symbolic link or junction is unlinked, and its target is left alone.
- **Confirmation is demanded only when there is something to remove.** With a non-empty plan, prune prompts, or fails with `confirmation_required` (exit `2`) under `--json`, `--quiet`, redirected stdin, or redirected stderr. With an empty plan it neither prompts nor fails: it exits `0`, so a scheduled prune on a clean cache is safe without `--yes`. In human output it prints `Removed 0 unused cached repositories.`; under `--json` it emits the envelope instead, and `--quiet` suppresses the human line.

A deletion that fails with an I/O or permission error produces `cache_prune_failed` (exit `1`). Prune never removes lock files from `locks/`.

## Common questions

### Deleting a cache directory by hand

This is safe when no `install` or `update` is running against it. Nothing reads a cache directory outside `install`, `update`, and the three `cache` commands, so removing one does not uninstall the tool that was built from it: that tool was installed from a NuGet package, not from the cache. `list` will keep reporting the old path until the next `update`, which re-clones into the same location. Deleting the whole cache root is equally safe and costs you a fresh clone per tool on the next run.

### What uninstall leaves behind

`uninstall` removes the global tool and the installation record and keeps the sources. Its success message says so, ending with `Cached sources retained at <PATH>.` when the installation record carries a repository path. Reclaiming that space is what `cache prune` is for.

### Why a repository is still listed after uninstall

`cache list` lists directories, not installation records. Once the record is gone the directory becomes unmanaged: it still appears, `cache show` reports `no` in its `Managed` field with `-` for every record-derived field, and `cache prune` will remove it.

### Why prune skipped a repository

Either it is protected, or it is locked. A repository is protected when an installation record names it, directly or through the canonical path recomputed from the record's source ID; run `dotnet git-tool cache show <REPOSITORY>` and check the `Managed` field. A repository is locked when another operation holds its repository lock, in which case its path appears in `skippedInUseRepositoryPaths` in the JSON envelope and a later prune removes it.

## See also

- [Documentation index](README.md)
- [Configuration](configuration.md)
- [CLI reference](cli-reference.md)
- [Troubleshooting](troubleshooting.md)
- [How it works](how-it-works.md)
