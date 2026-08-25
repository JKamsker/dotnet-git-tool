# Getting started

This walkthrough takes you from an empty machine to an installed source tool and back again. A **source
tool** is a .NET global tool that `dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as
`dotnet git-tool`) built for you out of a Git repository, instead of downloading it from a NuGet feed. The
example repository throughout is `JKamsker/bookmeta-cli`. Paths in the output blocks use the Linux defaults
listed in [Configuration](configuration.md), and the commit hashes, versions, and dates are illustrative and
differ on your machine.

## Before you start

`dotnet-git-tool` runs on Windows, macOS, and Linux. Four things have to be in place before an install
succeeds.

- A .NET 10 SDK. `dotnet-git-tool` targets `net10.0`, and its framework reference rolls forward across .NET 10
  patch and minor releases but not across a major version, so a machine carrying only .NET 11 does not run it.
  A runtime-only machine is not enough either: the install pipeline runs `dotnet msbuild` and `dotnet pack`
  against the repository you install from, and those need an SDK.
- `git` on `PATH`. Cloning and fetching the repository go through `git`. Without it, any command that clones,
  fetches, or inspects the repository cache fails with
  `error: Could not start 'git'. Make sure it is installed and available on PATH.`
- `dotnet` on `PATH`. Project evaluation, packing, and the global install all run through the `dotnet` driver,
  and the failure text is the same with `dotnet` in place of `git`.
- Network access to the NuGet feeds the target repository restores from. Packing runs a restore, so building
  `JKamsker/bookmeta-cli` downloads that repository's dependencies the way a local `dotnet build` of it would,
  and `dotnet tool install --global` queries your configured feeds as well.

The SDK that builds the target repository is the SDK on your machine, not one the repository ships. If the
repository pins an SDK version you do not have, the build fails the way any local build of that repository
would, with one narrow automatic retry described in [How it works](how-it-works.md).

## Install `dotnet-git-tool`

```console
dotnet tool install --global JKToolKit.Git.Tool
```

`dotnet tool install --global` puts the executable in the .NET global tools directory, which has to be on
your `PATH` before you can call it. Open a new shell after installing. If `dotnet git-tool` is still not
found, [Troubleshooting](troubleshooting.md) has the fix.

Verify the install:

```console
dotnet git-tool --version
```

Output (the value is whichever version you installed):

```text
0.0.1
```

`-v` is the short form, and both work only on the root command. Every subcommand accepts `-h` and `--help`.

## Step 1: preview with `--dry-run`

`install` builds the target repository, which means running code from that repository under your user
account. Preview first: `--dry-run` returns before the confirmation and before the clone, so it touches no
network and executes nothing.

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
```

Output:

```text
Would prepare cached sources for JKamsker/bookmeta-cli, discover a tool project, pack it for a 'dotnet <command>' invocation, install it globally, and retain clean sources at /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac.
```

The same preview as machine-readable data:

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run --json
```

That writes one JSON envelope to stdout, and two keys of its `data` object matter here.
`executesRepositoryCode` is a literal `true` in both preview payloads, the machine-readable form of the
warning at the top of this section; it does not appear in the payload of a real install or update.
`repositoryPath` is the exact cache directory the clone will land in. The envelope itself, the remaining
keys, and the fact that `source` gains an `@<REF>` suffix once you pin a ref are in
[the JSON envelope](cli-reference.md#the-json-envelope).

A preview reports from local state only: it does not check that the repository exists, that the ref exists on
the remote, or that `--project` is valid. A ref is still checked for shape, so a malformed one fails with
error kind `invalid_ref` and exit code `2` even in a preview. And `install --dry-run` still refuses a source
ID that already has an installation record, with
`error: 'JKamsker/bookmeta-cli' is already managed. Use 'dotnet git-tool update JKamsker/bookmeta-cli'.`

Unknown options are discarded rather than rejected. `--dryrun --yes` is a real installation, not a preview,
because the misspelled flag is dropped and `--yes` answers the confirmation. Confirm a preview by the
`Would prepare` line or `"action": "install"` in the output. [Security](security.md) covers the consequences.

## Step 2: install

```console
dotnet git-tool install JKamsker/bookmeta-cli --yes
```

Progress goes to stderr and the result goes to stdout. Together your terminal shows:

```text
Preparing cached repository for JKamsker/bookmeta-cli...
Discovering executable projects with MSBuild...
Packing src/AudioBookMeta.Tool/AudioBookMeta.Tool.csproj...
Installing git.JKamsker.bookmeta-cli 0.0.0-git.4fbe47e66359.dotnet globally...
Installed JKamsker/bookmeta-cli at 4fbe47e66359. Command: dotnet bookmeta. Clean sources: /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

In one sentence: `dotnet-git-tool` cloned the repository into the repository cache, found the executable
project with MSBuild, packed it into a NuGet package named `git.JKamsker.bookmeta-cli` at version
`0.0.0-git.4fbe47e66359.dotnet`, installed that package as a .NET global tool, and wrote an installation
record describing what it did.

Output from `git` and MSBuild is captured rather than streamed, so a long silence between those lines while
the clone and the build run is normal.

If a repository holds more than one executable project, this step stops with
`error: Found multiple executable projects: <PATHS>. Pass --project <PATH>.` instead. Pass
`--project src/AudioBookMeta.Tool/AudioBookMeta.Tool.csproj` to choose one;
[Troubleshooting](troubleshooting.md) covers that and the other first-run failures.

Without `--yes` on an interactive terminal you get a confirmation prompt on stderr instead:

```text
Warning: building 'JKamsker/bookmeta-cli' can execute arbitrary code from that repository.
Continue? [y/N]
```

Anything other than `y` or `yes`, in any casing, cancels with error kind `cancelled` and exit code `10`. When
stdin or stderr is redirected, or when you pass `--json` or `--quiet`, there is no prompt at all: the command
fails with error kind `confirmation_required` and exit code `2` unless `--yes` is present.
[Security](security.md) explains why the confirmation exists, and [Automation](automation.md) covers running
installs unattended.

## Step 3: run the installed command

The default command style exposes the tool as a `dotnet` subcommand:

```console
dotnet bookmeta --help
```

That output comes from `bookmeta` itself, not from `dotnet-git-tool`. The base name `bookmeta` is derived
from the repository rather than chosen by you; [Authoring tools](authoring-tools.md) covers how a repository
controls it.

## Step 4: see what you have

```console
dotnet git-tool list
```

Output:

```text
SOURCE                         PACKAGE                            COMMIT         COMMAND                  CACHE PATH
JKamsker/bookmeta-cli          git.JKamsker.bookmeta-cli          4fbe47e663597… dotnet bookmeta          /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

| Column | Meaning |
|---|---|
| `SOURCE` | The source ID, which is also the name you pass to `update` and `uninstall`. |
| `PACKAGE` | The generated package ID that `dotnet tool list --global` also shows. |
| `COMMIT` | The commit that was actually built. |
| `COMMAND` | What you type to run the tool. |
| `CACHE PATH` | The cached repository the build came from. |

The first three columns truncate with `…` when a value is too wide, so a 40-character commit shows as 13
characters plus the ellipsis. `list` prints the same table under `--quiet`, and `list --json` returns the
full untruncated records.

## Step 5: update

```console
dotnet git-tool update JKamsker/bookmeta-cli --yes
```

When the default branch has not moved, nothing is rebuilt:

```text
Refreshing cached repository for JKamsker/bookmeta-cli...
JKamsker/bookmeta-cli is already at 4fbe47e66359. Clean sources: /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

When there is a new commit, the message starts with `Updated`, names the new short commit and the
invocation, and the tool is repacked and reinstalled. Under `--json` the two outcomes are the `action`
values `unchanged` and `updated`, which is the reliable way to tell them apart in a script.

`update` reuses the project, clone URL, and command style recorded at install time. It uses the remote default
branch unless you supply a ref, so `update JKamsker/bookmeta-cli` needs no other arguments. No command updates
everything at once, and nothing updates itself in the background.

## Step 6: uninstall

```console
dotnet git-tool uninstall JKamsker/bookmeta-cli --yes
```

Output:

```text
Uninstalling git.JKamsker.bookmeta-cli...
Uninstalled JKamsker/bookmeta-cli (git.JKamsker.bookmeta-cli). Cached sources retained at /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac.
```

`uninstall` removes the global tool and the installation record. It deliberately keeps the cached
repository, so reinstalling later does not clone again. Reclaim that disk with `cache prune`, which removes
only cached repositories that no installation record refers to:

```console
dotnet git-tool cache prune --dry-run
```

Output:

```text
Would remove 1 unused cached repository:
  /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

Then remove it:

```console
dotnet git-tool cache prune --yes
```

Output:

```text
Removed 1 unused cached repository.
```

`cache prune` asks for confirmation only when it would delete something, so running it against an already
clean cache succeeds without `--yes`.

## Variations

Neither of these is part of the basic loop. Read them when you need them.

### Pinning to a tag or commit

Add `@` and a ref to the repository argument to install a specific branch, tag, or commit:

```console
dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0 --yes
```

For an installation you already have, `--ref` does the same job. Preview it first:

```console
dotnet git-tool update JKamsker/bookmeta-cli --ref v1.2.0 --dry-run
```

Output:

```text
Would refresh cached sources for JKamsker/bookmeta-cli@v1.2.0, rebuild src/AudioBookMeta.Tool/AudioBookMeta.Tool.csproj for a 'dotnet <command>' invocation, update git.JKamsker.bookmeta-cli globally, and retain clean sources at /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac.
```

Three things change once you apply a pin. The cached repository is checked out detached at the fetched ref;
`dotnet git-tool cache show JKamsker/bookmeta-cli` reports the checked-out state and the ref that is pinned,
and [Repository cache](repository-cache.md) covers that command. The ref is stored in the installation record
and remains there until the next update records its selection. Repeat the ref on each later update to remain
pinned. And clones are shallow, so a bare commit SHA that a depth-1 fetch cannot reach may fail where a tag or
branch name succeeds.

To clear a pin and go back to the default branch, run `update` without a ref. An
explicit `--ref` always beats a ref embedded in the argument, and the `@ref` suffix is only read from the
`owner/repo` form, so with an SSH or HTTP(S) URL `--ref` is the only way to pin.

### Switching the command style

The default is dotnet style, which exposes `dotnet bookmeta`. Standalone style exposes `bookmeta` with no
prefix. Switch an existing installation with `--standalone`:

```console
dotnet git-tool update JKamsker/bookmeta-cli --standalone --yes
```

Output:

```text
Refreshing cached repository for JKamsker/bookmeta-cli...
Packing src/AudioBookMeta.Tool/AudioBookMeta.Tool.csproj...
Updating git.JKamsker.bookmeta-cli to 0.0.0-git.4fbe47e66359.standalone...
Updated JKamsker/bookmeta-cli to 4fbe47e66359. Command: bookmeta. Clean sources: /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

The style is baked into the generated package version: the same commit produces
`0.0.0-git.4fbe47e66359.dotnet` in dotnet style and `0.0.0-git.4fbe47e66359.standalone` in standalone style.
Changing the style therefore always repacks and reinstalls, even at an identical commit, and never reports
`unchanged`. `--dotnet-command` switches back. Passing both flags at once fails with error kind
`invalid_command_style`. [How it works](how-it-works.md) derives the version string.

## What got created on your machine

`dotnet-git-tool` writes to two locations of its own, a cache root that holds the retained clones and a state
directory that holds `installed.json`. They resolve independently, which is why they diverge on macOS: the
cache root is `~/.cache/dotnet-git-tool` there, not `~/Library/Caches`, while `installed.json` follows the
platform application-support directory. [Configuration](configuration.md) lists the resolved path for each
platform and the environment variables that move them.

The tool you installed lands in a third place, the .NET global tools directory, which
`dotnet tool install --global` manages and `dotnet tool uninstall --global` reclaims.

To get back to a clean machine, in this order:

1. Uninstall each source tool:

   ```console
   dotnet git-tool uninstall JKamsker/bookmeta-cli --yes
   ```

2. Remove the cached repositories no installation record refers to any more:

   ```console
   dotnet git-tool cache prune --yes
   ```

3. Delete the cache root and the state directory, whose paths are in [Configuration](configuration.md).
   `cache prune` empties `repositories/` but never removes the lock files under `locks/`, and it never
   touches the state directory, so both roots survive step 2.

4. Uninstall `dotnet-git-tool` itself:

   ```console
   dotnet tool uninstall --global JKToolKit.Git.Tool
   ```

## See also

- [Documentation index](README.md)
- [CLI reference](cli-reference.md)
- [Security](security.md)
- [Troubleshooting](troubleshooting.md)
