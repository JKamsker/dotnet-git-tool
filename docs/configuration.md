# Configuration

`dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`) has no configuration file of its own. Four environment variables relocate what it writes, a fifth affects styling, and the only files it maintains outside the repository cache are `installed.json` and its lock. This page documents those variables, both precedence chains, the per-platform default locations, and the shape of the installation state file.

Example paths use each platform's default location. Substitute your own user name for `you` and `You`.

Behavior also changes from outside the command line in one way this page does not own. `dotnet-git-tool` starts `git` and `dotnet` as child processes without adding, removing, or overriding a single environment variable, so your Git configuration, credential helper, SSH agent, NuGet configuration, and any proxy variables reach every clone, restore, and pack it runs. The repository being installed is the other outside influence: it can carry a [repository manifest](#the-repository-manifest), and its `global.json` drives the SDK fallback described in [How it works](how-it-works.md).

## Environment variables

Four environment variables move a directory `dotnet-git-tool` writes to. A fifth, `NO_COLOR`, affects styling and is read by Spectre.Console rather than by `dotnet-git-tool` itself.

| Variable | What it sets | If unset |
|---|---|---|
| `DOTNET_GIT_TOOL_CACHE` | The cache root directory itself | `XDG_CACHE_HOME` is tried, then the platform default cache root |
| `XDG_CACHE_HOME` | The parent of the cache root, giving `<XDG_CACHE_HOME>/dotnet-git-tool` | The platform default cache root |
| `DOTNET_GIT_TOOL_HOME` | The state directory that holds `installed.json` | `XDG_DATA_HOME` is tried, then the platform default state directory |
| `XDG_DATA_HOME` | The parent of the state directory, giving `<XDG_DATA_HOME>/dotnet-git-tool` | The platform default state directory |
| `NO_COLOR` | ANSI styling in the `dotnet git-tool cache list` table | Spectre.Console renders that table as usual |

Rules that apply to all four directory variables:

- A variable that is unset, empty, or whitespace only is ignored, and resolution falls through to the next step.
- All four name directories, never files. `DOTNET_GIT_TOOL_CACHE` and `DOTNET_GIT_TOOL_HOME` are used exactly as given, so `installed.json` is created inside `DOTNET_GIT_TOOL_HOME` rather than at that path. `XDG_CACHE_HOME` and `XDG_DATA_HOME` get a `dotnet-git-tool` segment appended.
- Setting a `DOTNET_GIT_TOOL_*` variable makes the matching `XDG_*` variable irrelevant, because the specific one is checked first. Set one or the other, never both.
- `DOTNET_GIT_TOOL_CACHE` and `DOTNET_GIT_TOOL_HOME` are passed through `Path.GetFullPath`, so a value containing `..` or a trailing separator is normalized to a full path. `XDG_CACHE_HOME` and `XDG_DATA_HOME` are combined with the `dotnet-git-tool` segment without normalization.
- A relative value resolves against the current working directory, which makes the resolved location depend on where you happened to run the command. Set all four to absolute paths.
- The values are read once, when the process starts. Changing a variable affects the next invocation, not a running one.

The only output rendered through Spectre.Console is the `cache list` table, and that table applies no color of its own. Everything else `dotnet-git-tool` prints is plain text. Spectre.Console honors `NO_COLOR` for the table.

### Setting a variable

Moving the repository cache to another drive takes one variable.

Linux and macOS:

```bash
export DOTNET_GIT_TOOL_CACHE=/mnt/data/dotnet-git-tool/cache
```

Windows (PowerShell):

```powershell
$env:DOTNET_GIT_TOOL_CACHE = "D:\dotnet-git-tool\cache"
```

Both forms last for the current shell session only. To make a change permanent, set the variable in your shell profile on Linux and macOS, or in your user environment variables on Windows.

Both XDG variables are honored on Windows as well, because each chain checks them before falling back to a platform default. On Windows, `XDG_CACHE_HOME` cannot reproduce the platform default cache root: it always yields `<XDG_CACHE_HOME>/dotnet-git-tool`, while the Windows default carries an extra `cache` segment.

## Cache root resolution

The cache root is the top directory of the repository cache. It is resolved in this order:

1. `DOTNET_GIT_TOOL_CACHE`, if set to a non-empty value. The value becomes the cache root.
2. `XDG_CACHE_HOME`, if set to a non-empty value. The cache root becomes `<XDG_CACHE_HOME>/dotnet-git-tool`.
3. The per-platform default below.

| Platform | Default cache root |
|---|---|
| Linux | `/home/you/.cache/dotnet-git-tool` |
| macOS | `/Users/you/.cache/dotnet-git-tool` |
| Windows | `C:\Users\You\AppData\Local\dotnet-git-tool\cache` |

macOS uses `~/.cache/dotnet-git-tool`, the same location as Linux. It does not use `~/Library/Caches`. The non-Windows branch builds the path from the user profile directory plus `.cache`.

To confirm which cache root is in effect, run `dotnet git-tool cache list --json` and read `data.repositoryRoot`, which is the resolved cache root plus `/repositories`. The human `cache list` table does not show it.

Moving the cache root does not move what is already there. `cache prune` only enumerates the `repositories` directory under the resolved cache root, so cached repositories under the old root become invisible to it and have to be deleted by hand. `list` keeps reporting the old `repositoryPath` for each installed source tool until the next `update` re-clones into the new root and rewrites the record.

What lives under the cache root, how cached repositories are named, and how to inspect or prune them is covered in [Repository cache](repository-cache.md).

## State directory resolution

The state directory holds `installed.json`, its lock file, and its temporary files. It is resolved in this order:

1. `DOTNET_GIT_TOOL_HOME`, if set to a non-empty value. The value becomes the state directory, and `installed.json` is created inside it.
2. `XDG_DATA_HOME`, if set to a non-empty value. The state directory becomes `<XDG_DATA_HOME>/dotnet-git-tool`.
3. The platform local application data directory plus `dotnet-git-tool`.

| Platform | Default installation state file |
|---|---|
| Linux | `/home/you/.local/share/dotnet-git-tool/installed.json` |
| macOS | `/Users/you/Library/Application Support/dotnet-git-tool/installed.json` |
| Windows | `C:\Users\You\AppData\Local\dotnet-git-tool\installed.json` |

On macOS the two locations sit under different parents. With no environment variables set, state lives in `~/Library/Application Support/dotnet-git-tool` while cached repositories live in `~/.cache/dotnet-git-tool`. Step 3 asks .NET for the local application data directory, which .NET maps to `~/Library/Application Support` on macOS and to `~/.local/share` on Linux, while the cache root is built from the user profile plus `.cache`.

No command prints the state file path on success. It appears in output only inside an `invalid_state` error message, so to confirm which state directory is in effect, look for `installed.json` at the location the list above resolves to.

The directory is created on demand, at the first write. A missing state directory or a missing `installed.json` means "no installations", not an error.

## The installation state file

`installed.json` is the record of every source tool `dotnet-git-tool` has installed. It is the only thing that makes an installation managed: `list`, `update`, `uninstall`, and the `Managed` field of `cache show` all read it.

The file has two top-level keys. `schemaVersion` is the state schema version and is always `1`. `installations` is the array of installation records.

```json
{
  "schemaVersion": 1,
  "installations": [
    {
      "sourceId": "JKamsker/bookmeta-cli",
      "cloneUrl": "https://github.com/JKamsker/bookmeta-cli.git",
      "requestedRef": null,
      "project": "src/BookMeta.Cli/BookMeta.Cli.csproj",
      "packageId": "git.JKamsker.bookmeta-cli",
      "version": "0.0.0-git.4fbe47e66359.dotnet",
      "commit": "4fbe47e663597fb0da63f344373cfeeee99c6a26",
      "command": "dotnet bookmeta",
      "commandStyle": "dotnet",
      "repositoryPath": "/home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac",
      "installedAt": "2026-08-12T18:04:11.2233445+00:00",
      "updatedAt": null
    }
  ]
}
```

Each element of `installations` is one installation record, and it is the same object `--json` returns as `installation` and inside `installations`. The [installation record keys](cli-reference.md#installation-record-keys) table in the CLI reference documents every field. `updatedAt` is the only field declared optional; a record written by an older build that omits it loads cleanly and reads back as `null`. Nothing else in the file is validated beyond `schemaVersion`.

`dotnet-git-tool` reads only `schemaVersion: 1`. A file whose `schemaVersion` is missing or any other number is rejected with the error kind `invalid_state` and exit code `1`, and that failure blocks every command that reads state: `install`, `update`, `uninstall`, and `list`, plus `cache list`, `cache show`, and `cache prune` whenever the cache root already holds a `repositories` directory. The three `cache` commands return before touching the state file when that directory does not exist yet. See [Troubleshooting](troubleshooting.md) for the recovery steps.

### How writes are made safe

Every write is serialized and atomic:

- Before each write, `dotnet-git-tool` takes an exclusive lock on `installed.lock` in the state directory, retrying every 100 milliseconds for up to 10 seconds. On timeout it fails with the error kind `state_locked` and exit code `6`.
- The new content is serialized to a temporary `installed-<GUID>.tmp` file in the same directory, then moved over `installed.json` with overwrite. A crash mid-write leaves the previous file intact.
- The lock file is created if it does not exist and is not deleted afterwards. A zero-byte `installed.lock` sitting in the state directory is normal.
- Reads take no lock, so a read never waits for a writer.

### Editing or deleting the file

> [!WARNING]
> Hand-editing `installed.json` is not supported. Malformed JSON or an unexpected `schemaVersion` takes down every command that reads state, not only `list`.

Deleting the file is recoverable but not a full uninstall. `dotnet-git-tool` forgets the installations, and each affected tool then reports `installation_not_found` on `update` and `uninstall`. The .NET global tools themselves stay installed and keep working, because they live in the global tools directory, which none of the variables on this page move. Remove each one by its `packageId`:

```console
dotnet tool uninstall --global git.JKamsker.bookmeta-cli
```

Copy the `packageId` values out of `installed.json` or run `dotnet git-tool list` before deleting anything. Cached repositories are also unaffected by deleting the state file, and every one of them becomes prunable once no record protects it.

Reinstalling a forgotten source tool means removing the global tool first. `install` always runs `dotnet tool install --global` against the same generated package ID, so running it while the old package is still installed fails with the error kind `child_process_failed` and exit code `1`.

## The repository manifest

One configuration input does not come from your machine. A repository can carry a repository manifest at `.config/dotnet-git-tool.json`, committed by its author, which selects the project to build and the command name to expose. You do not create it as the installing user. This is not a .NET tool manifest (`dotnet-tools.json`); `dotnet-git-tool` does not read or write those. See [Authoring tools](authoring-tools.md) for the schema, the binding rules, and how the manifest interacts with `--project`.

## Isolating a run

Pointing both directory variables at throwaway directories gives you a run that touches neither your real repository cache nor your real installation state.

Linux and macOS:

```bash
export DOTNET_GIT_TOOL_CACHE=/home/you/scratch/dotnet-git-tool/cache
export DOTNET_GIT_TOOL_HOME=/home/you/scratch/dotnet-git-tool/state
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
dotnet git-tool cache list
```

Windows (PowerShell):

```powershell
$env:DOTNET_GIT_TOOL_CACHE = "C:\Users\You\scratch\dotnet-git-tool\cache"
$env:DOTNET_GIT_TOOL_HOME = "C:\Users\You\scratch\dotnet-git-tool\state"
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
dotnet git-tool cache list
```

`--dry-run` clones nothing, so the isolated cache stays empty and the last command prints:

```text
No cached repositories found.
```

That is the expected result of the sequence above, not a sign that the isolation failed. The isolated cache fills up on the first real install.

Neither directory needs to exist beforehand. `dotnet-git-tool` creates the cache root and the state directory the first time it writes to them, so a sequence that is only `--dry-run` and `cache list` creates nothing at all. Delete both directories when you are done with a real isolated install.

> [!IMPORTANT]
> Isolation covers the repository cache and the installation state only. A real install inside an isolated run still calls `dotnet tool install --global`, so the source tool lands in your normal global tools directory and has to be removed with `dotnet tool uninstall --global <PACKAGE_ID>`. Pair the isolated variables with `--dry-run` if you want a run that installs nothing.

The same two variables are how you give a CI job an ephemeral or cache-restored directory. [Automation](automation.md) shows that in a workflow.

## What is not configurable

None of the following exists.

- There is no global, per-user, or per-project configuration file for `dotnet-git-tool`. Behavior comes from command-line flags, the environment variables above, and the repository manifest the tool author committed.
- There is no default-ref setting and no way to clear a pin. `install` uses the remote default branch unless you pass `--ref` or the `owner/repo@ref` form. `update` reuses the ref recorded at install time unless you pass a new one, and it records the new one, so a pin persists across updates. To return a pinned source tool to the default branch, uninstall it and install it again.
- `dotnet-git-tool` has no proxy, credential, or authentication settings of its own. Repository access goes through `git`, so your Git configuration, credential helper, and SSH agent decide what works there. Restoring and installing the generated package goes through `dotnet`, so your NuGet configuration decides what works there.
- There is no way to change the generated package ID prefix. It is always `git.` followed by the derived source ID, and the version is always derived from the commit and the command style. [How it works](how-it-works.md) covers the derivation.
- `dotnet-git-tool` offers no option that changes where the source tool lands. It always uses the `--global` form of `dotnet tool install`, `dotnet tool update`, and `dotnet tool uninstall`, so the location is whatever the `dotnet` driver uses for global tools.
- `--no-color` is accepted on every command and does nothing. The flag is parsed and never read, so it changes no output, including the `cache list` table. `NO_COLOR` reaches that table through Spectre.Console instead.

## See also

- [Documentation index](README.md)
- [Repository cache](repository-cache.md)
- [CLI reference](cli-reference.md)
- [Automation](automation.md)
- [Troubleshooting](troubleshooting.md)
