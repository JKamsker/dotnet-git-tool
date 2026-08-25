# Troubleshooting

`dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`) reports every failure it recognizes as a single line on stderr in human output, prefixed with `error: `. With `--json` the same text appears as `error.message` on stdout next to a stable `error.kind`. Rerun a failing command with `--json` when you want the exact kind.

The first part of this page is symptom-first: what you see, why it happens, and what to do. The second part indexes every error kind with the message text it produces and the first thing to try. The third part shows how to get more detail out of a failing run.

Paths in the examples use the Linux defaults. Substitute the cache root and state directory for your platform from [Configuration](configuration.md). Commit hashes, versions, sizes, and dates in sample output are illustrative and differ on your machine.

## Common problems

Human output prints the message only, never the error kind, so match the text you got against this table and read the section it names. Rerunning with `--json` gives you `error.kind` directly. Messages whose kind has no section of its own route to the error index further down, which lists every message text alongside its kind.

| Message text | Section |
|---|---|
| `Could not start '` | `git` or `dotnet` is not on `PATH` |
| `can execute arbitrary repository code` or `requires confirmation` | A script or CI job fails with `confirmation_required` |
| `is already managed` | The repository is already installed |
| `is not managed` | Nothing is recorded for that repository |
| `Found multiple`, `No executable project`, `No .csproj files`, `must be inside`, `exactly one .csproj`, `Project '<PATH>' was not found.` | The project cannot be chosen |
| `is not executable. Expected OutputType=Exe` | The selected project is not a program |
| Anything ending in `failed:` or `failed with exit code` | The build fails inside the target repository |
| `Cache path '` | The cache path is occupied |
| `Timed out waiting` | A lock timeout after a long pause |
| `is still dirty after cleanup` | The cached repository stays dirty |
| `Could not remove cached repository` | `cache prune` cannot delete a directory |
| `Could not read state file` | Every command fails with `Could not read state file` |
| `Repository must be an owner/repo`, `Repository URLs must include`, `The requested ref is not a valid`, `cannot be used together`, `Invalid .config/dotnet-git-tool.json`, `cannot be exposed as a .NET tool command` | Error index |
| `MSBuild returned an unreadable project evaluation`, `did not report a default branch`, `Cached repository '`, `is ambiguous. Use one of:`, `Refusing to prune path outside`, `Operation cancelled.` | Error index |

### `git` or `dotnet` is not on `PATH`

Symptom: exit `5`, kind `dependency_not_found`, before anything is cloned or built:

```text
error: Could not start 'git'. Make sure it is installed and available on PATH.
error: Could not start 'dotnet'. Make sure it is installed and available on PATH.
```

Why: `dotnet-git-tool` runs `git` and `dotnet` as external commands for every clone, every project evaluation, and every pack. Neither is bundled. Packing needs a full .NET SDK, so a machine with only the runtime cannot install a source tool.

Fix: install whichever is missing, confirm with `git --version` and `dotnet --version`, then rerun.

### `dotnet git-tool` is not found after installing the package

Symptom: `dotnet tool install --global JKToolKit.Git.Tool` reports success, but `dotnet git-tool --version` makes the `dotnet` driver report that the specified command or file was not found and that a `dotnet`-prefixed executable with that name is not on `PATH`. That message comes from the .NET SDK, not from `dotnet-git-tool`, and it is translated into your system language.

Why: `dotnet tool install --global` puts the executable in the .NET global tools directory, and the `dotnet` driver finds it only when that directory is on `PATH`. A shell that was already open when you installed still has the old `PATH`.

Fix: open a new shell and try again. If it still fails, add the global tools directory to `PATH`.

| Platform | .NET global tools directory |
|---|---|
| Linux | `/home/you/.dotnet/tools` |
| macOS | `/Users/you/.dotnet/tools` |
| Windows | `C:\Users\You\.dotnet\tools` |

### A script or CI job fails with `confirmation_required`

Symptom: a command that works in a terminal exits `2` in a script, with one of these messages:

```text
error: Building 'JKamsker/bookmeta-cli' can execute arbitrary repository code. Inspect with --dry-run or explicitly consent with --yes.
error: Uninstalling 'JKamsker/bookmeta-cli' requires confirmation. Inspect with --dry-run or confirm with --yes.
error: Removing 3 unused cached repositories requires confirmation. Inspect with --dry-run or explicitly confirm with --yes.
```

Why: `dotnet-git-tool` refuses the confirmation prompt instead of showing it whenever `--json` or `--quiet` is passed, or whenever stdin or stderr is redirected. Redirection alone is enough. A command that passed neither `--json` nor `--quiet` still fails this way as soon as its stdin or stderr is captured, which is what a CI job, a shell pipeline, and any output-capturing harness do.

Fix: pass `-y` (`--yes`) on `install`, `update`, `uninstall`, and `cache prune` in any non-interactive context. Preview first with `--dry-run`, which never prompts. `cache prune` is the one exception to the rule: with nothing to remove it skips the confirmation entirely and exits `0` without `--yes`. See [Automation](automation.md) for CI recipes and [Security](security.md) for why the gate exists.

### The repository is already installed (`already_installed`)

Symptom: exit `6`, including under `--dry-run`, because the check runs before the preview.

```text
error: 'JKamsker/bookmeta-cli' is already managed. Use 'dotnet git-tool update JKamsker/bookmeta-cli'.
```

Why: an installation record already exists for that source ID. Source IDs are compared without regard to case, and the `owner/repo`, SSH, and HTTP(S) URL forms of the same GitHub repository all normalize to the same source ID, so a different spelling still matches.

Fix: run `update` instead of `install`. There is no force flag. To start over completely, run `uninstall` and then `install`. To clear a pinned ref, run `update` without a ref; it switches to the remote default branch.

### Nothing is recorded for that repository (`installation_not_found`)

Symptom: exit `5`. `update` and `uninstall` word it differently:

```text
error: 'JKamsker/bookmeta-cli' is not managed. Install it first with 'dotnet git-tool install JKamsker/bookmeta-cli'.
error: 'JKamsker/bookmeta-cli' is not managed by dotnet git-tool.
```

Why: no installation record matches the source ID your argument normalized to. Usually the repository was never installed with this tool. It also happens when the argument does not normalize to the recorded ID: `uninstall` falls back to the trimmed raw text when it cannot parse the argument, so a typo is looked up literally.

Fix: list what is actually recorded and copy the value from the `SOURCE` column.

```console
dotnet git-tool list
```

The human `SOURCE` column truncates at 30 characters with `…`. When the value is longer, read `data.installations[].sourceId` from `dotnet git-tool list --json`.

### The project cannot be chosen (`ambiguous_project`, `project_not_found`)

Symptom: the run reaches project discovery and stops. Ambiguity is exit `2`; nothing found is exit `5`. The messages take these forms:

```text
error: Found multiple PackAsTool projects: <PATHS>. Pass --project <PATH>.
error: Found multiple executable projects: <PATHS>. Pass --project <PATH>.
error: No executable project was found. Pass --project to select a project explicitly.
error: No .csproj files were found in the repository.
```

Why: `dotnet-git-tool` picks a project only when the choice is unambiguous. It refuses to guess between several projects marked `PackAsTool=true`, or between several projects with `OutputType=Exe` when none is marked `PackAsTool`. Nothing found means every project is a library, or the repository holds no `.csproj` at all. Only `*.csproj` is discovered, so F# and Visual Basic projects are never candidates.

Fix, in order of what you control. First, pass `--project` with a repository-relative path that stays inside the cached repository:

```console
dotnet git-tool install JKamsker/bookmeta-cli --yes --project src/BookMeta.Cli/BookMeta.Cli.csproj
```

A directory works too, if it holds exactly one `.csproj`. A rejected `--project` value produces one of these messages, the first two at exit `2` with kind `invalid_project` and the third at exit `5` with kind `project_not_found`:

```text
error: The selected project must be inside the cloned repository.
error: Directory '<PATH>' must contain exactly one .csproj file; found <N>.
error: Project '<PATH>' was not found.
```

If you own the repository, two further fixes are available: set `PackAsTool=true` on exactly one project, or commit a repository manifest at `.config/dotnet-git-tool.json` with a `project` field. This is not a .NET tool manifest (`dotnet-tools.json`); `dotnet-git-tool` does not read or write those. [Authoring a tool repository](authoring-tools.md) covers both.

### The selected project is not a program (`project_not_executable`)

Symptom: exit `2` after you passed `--project` or the repository manifest set one. The message takes this form:

```text
error: Selected project '<PATH>' is not executable. Expected OutputType=Exe or PackAsTool=true.
```

Why: an explicit project skips the ranking, but the project you name still has to produce a runnable program. A class library qualifies for neither `OutputType=Exe` nor `PackAsTool=true`.

Fix: point `--project` at the console project rather than the library it references. You can check a project without installing anything by running this in your own clone:

```console
dotnet msbuild src/BookMeta.Cli/BookMeta.Cli.csproj -getProperty:OutputType,PackAsTool
```

### The installed command is not found, but the install succeeded

Symptom: `install` reports success, yet typing `bookmeta` fails. The success line names the invocation in the middle, not at the end:

```text
Installed JKamsker/bookmeta-cli at 4fbe47e66359. Command: dotnet bookmeta. Clean sources: /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

Why: there are two command styles. The default dotnet style packages the command as `dotnet-bookmeta` and you invoke it as `dotnet bookmeta`. Standalone style packages it as `bookmeta` and you invoke it as `bookmeta`. The success line always prints the invocation that actually exists.

Fix: use the invocation from the `Command:` clause of the success line, or read the `COMMAND` column of `dotnet git-tool list`. To switch an existing installation to standalone style:

```console
dotnet git-tool update JKamsker/bookmeta-cli --yes --standalone
```

The style is part of the generated package version, so switching always repacks and never reports `unchanged`. A standalone command lands in the same global tools directory as `dotnet-git-tool` itself, so the `PATH` advice above applies to it as well.

### An old installation switches command style on update

Symptom: `update` reinstalls a source tool under the other command style. A tool you ran as `dotnet bookmeta` comes back as `bookmeta`, or the reverse.

Why: `update` reuses the recorded command style when you pass no style flag. An installation record written before `commandStyle` existed carries none, so the style is inferred from the recorded command: dotnet style only when that command starts with `dotnet ` or `dotnet-`, otherwise standalone.

Fix: pass the style you want on the next `update`. The record then stores it, and later updates keep it.

```console
dotnet git-tool update JKamsker/bookmeta-cli --yes --dotnet-command
```

### Two source tools want the same command

Symptom: either the .NET SDK refuses the install, at exit `1` with kind `child_process_failed` and a message of the form `Installing <PACKAGE_ID> failed: <LAST_LINE_FROM_THE_SDK>`, or a command that used to run one repository's tool now runs another's.

Why: the base name comes from the manifest's `command` field, then the project's `ToolCommandName`, then its `AssemblyName`, and `dotnet-git-tool` does not make it unique across repositories. Two repositories whose projects resolve to the same base name ask the .NET global tool installation for the same packaged command name, even though their generated package IDs differ.

Fix: find out which package owns the command, then install one of the two in the other command style so the packaged names differ. Dotnet style exposes `dotnet-bookmeta`; standalone style exposes `bookmeta`.

```console
dotnet tool list --global
dotnet git-tool update JKamsker/bookmeta-cli --yes --standalone
```

The base name itself can only be changed in the source repository. [Authoring a tool repository](authoring-tools.md) covers picking a distinctive one.

### `list` and the installed tools disagree

Symptom: `dotnet git-tool list` reports a source tool whose command does not run, or `uninstall` fails at exit `1` with kind `child_process_failed` and a message of the form `Uninstalling <PACKAGE_ID> failed: <LAST_LINE_FROM_THE_SDK>`.

Why: the installation record and the global tool are separate pieces of state. `install` rolls back, uninstalling the package again when the record cannot be written. `update` has no rollback, so a failed record write leaves the newer package installed while the record still describes the previous commit. Running `dotnet tool uninstall --global` by hand strands the record the other way round.

Fix: compare the two lists, matching the `PACKAGE` column against the package IDs the SDK reports.

```console
dotnet git-tool list
dotnet tool list --global
```

- When the record lags the installed package, run `update` again to reconcile it.
- When the global tool is already gone and only the record remains, `dotnet git-tool uninstall` cannot clear it, because it runs `dotnet tool uninstall --global` first and that step fails. Remove the entry from `installed.json` by hand; [Configuration](configuration.md) documents the file shape.

### The build fails inside the target repository

Symptom: exit `1`, kind `child_process_failed`. The message takes one of these two forms, the second when the child process printed nothing:

```text
error: <OPERATION> failed: <LAST_OUTPUT_LINE>
error: <OPERATION> failed with exit code <N>.
```

Why: every failed `git` or `dotnet` process produces this kind, and `<OPERATION>` names the step that failed.

| Operation prefix | What failed | Where to look |
|---|---|---|
| `Evaluating <PATH>`, `Packing <PATH>` | The target repository's own project evaluation or build. | Reproduce the build by hand, below. |
| `Cloning <SOURCE_ID>`, `Fetching ref <REF>`, `Checking out ref <REF>`, `Resolving the remote default branch`, `Fetching origin/<BRANCH>`, `Checking out origin/<BRANCH>` | Network access, authentication, or a ref the remote will not serve. | The private repository and commit SHA sections. |
| `Refreshing the cached repository origin`, `Validating the cached repository origin`, `Resolving the cached commit`, `Resetting the cached repository`, `Resetting cached submodules`, `Cleaning build artifacts from the cached repository`, `Verifying the cached repository` | A `git` command run against the cached repository. | The cache sections below, and [Repository cache](repository-cache.md). |
| `Installing <PACKAGE_ID>`, `Updating <PACKAGE_ID>`, `Uninstalling <PACKAGE_ID>` | The .NET SDK refusing the global tool operation. | The command collision and record mismatch sections above. |

The [CLI reference](cli-reference.md#error-kinds) lists every operation prefix verbatim. Most other error kinds are `dotnet-git-tool`'s own validation; the exceptions are `project_evaluation_failed`, which is MSBuild output the tool could not parse, and `repository_cache_dirty`, where the tree was still modified after cleanup.

Only one line survives. `dotnet-git-tool` captures the child process output and reports the last non-empty line, preferring stderr over stdout. Child output is never streamed, which is also why a long clone or build prints nothing while it runs. `--verbose` adds the tool's own diagnostics and no build output.

Fix: reproduce the build by hand in the cached repository. Get the path first:

```console
dotnet git-tool cache show JKamsker/bookmeta-cli
```

Then run a Release pack of the same project in that directory:

```console
cd /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
dotnet pack src/BookMeta.Cli/BookMeta.Cli.csproj --configuration Release -p:PackAsTool=true
```

Keep `-p:PackAsTool=true`, because `dotnet-git-tool` forces it on every pack: a project that fails only under that property packs cleanly without it. The tool additionally sets the package ID, the version, and the packaged command name, and [How it works](how-it-works.md) shows the full command.

> [!WARNING]
> Anything you leave inside a cache directory is deleted the next time `dotnet-git-tool` uses it, including ignored files such as `bin/` and `obj/`. Copy the directory elsewhere if you want to keep your changes.

### The repository pins an SDK you do not have

Symptom: `Evaluating <PATH> failed:` or `Packing <PATH> failed:` with a detail line mentioning a missing SDK, typically the `https://aka.ms/dotnet/sdk-not-found` link. Only the last line of the SDK's message is surfaced, so that link does not always reach you.

Why: the repository ships a `global.json` that pins an SDK version your machine does not have. `dotnet-git-tool` retries the same command once from outside the cached repository, but only when you already have a strictly newer SDK. Every other build failure is reported as-is. [How it works](how-it-works.md) describes the retry conditions.

Fix: install the pinned SDK version, or any newer one. `dotnet --list-sdks` lists what you already have.

### Pinning a commit SHA fails

Symptom: exit `1`, kind `child_process_failed`, and a message of the form `Fetching ref <REF> failed: <LAST_LINE_FROM_GIT>`, when the ref you pinned is a full commit hash.

Why: clones are shallow, so a raw commit hash is usually unreachable while a branch or tag name is fetched by name. [Repository cache](repository-cache.md) documents the clone and fetch commands.

Fix: pin a tag or a branch instead, either embedded or as a flag.

```console
dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0 --yes
dotnet git-tool install JKamsker/bookmeta-cli --ref v1.2.0 --yes
```

An explicit `--ref` overrides a ref embedded in the argument. The `@ref` suffix is parsed only from the `owner/repo` form, so URL and SSH inputs need `--ref`.

### The cache path is occupied (`invalid_repository_cache`, `repository_cache_conflict`)

Symptom: exit `6` before anything is cloned. The messages take these forms:

```text
error: Cache path '<PATH>' exists but is not a Git repository. Move it aside and retry.
error: Cache path '<PATH>' belongs to a different remote ('<ORIGIN>'). Move it aside and retry.
```

Why: one source ID always maps to one cache directory, and `dotnet-git-tool` never reclaims an occupied cache path automatically. It stops and tells you. The first message means something else occupies that path; the second means the directory is a clone of a different remote. Two remotes can collide because only the last two path segments of a URL form the source ID: the general form is `https://<HOST>/<GROUP>/<OWNER>/<REPOSITORY>.git`, and two URLs differing only in `<GROUP>` both produce the source ID `<HOST>/<OWNER>/<REPOSITORY>`.

Fix: the message contains the path. Move or delete that directory yourself and rerun; the next run reclones. `dotnet git-tool cache list` shows what is currently in the repository cache.

A genuine source ID collision has no workaround. The source ID is also the installation record key and the input to the generated package ID, so the second repository fails with `already_installed` (exit `6`) before the cache is consulted, and only one of the two can be managed at a time. Isolating both `DOTNET_GIT_TOOL_CACHE` and `DOTNET_GIT_TOOL_HOME` gives each its own cache and its own records, but both still generate the same package ID.

That refusal protects one cache path during a run, not the cache root as a whole: `cache prune` deletes every direct child of `<CACHE_ROOT>/repositories` that no installation record references, whether or not `dotnet-git-tool` created it. Keep nothing of your own anywhere under the cache root.

### A lock timeout after a long pause (`repository_cache_locked`, `state_locked`)

Symptom: exit `6` after a pause:

```text
error: Timed out waiting for another operation using this repository cache.
error: Timed out waiting for another dotnet git-tool operation to finish.
```

Why: `install` and `update` take two locks; `uninstall` takes only the installation state lock. The repository lock covers one cached repository and is waited on for 30 seconds. The state lock covers `installed.json`, is taken only by a write, and is waited on for 10 seconds.

Both locks are held as open file handles, so the operating system releases them when the holding process exits, including a process that was killed. Lock files under `<CACHE_ROOT>/locks` are zero bytes, are not removed by `cache prune`, and are harmless.

Fix:

- Wait for the other run to finish, then rerun.
- A timeout that keeps recurring usually means another `dotnet-git-tool` run is still going. It can also mean another program is holding the lock file open. Find and stop it rather than deleting anything.
- `cache prune` never waits: it skips a locked repository, reports it as skipped, and still exits `0`.

### The cached repository stays dirty (`repository_cache_dirty`)

Symptom: exit `1`, kind `repository_cache_dirty`. The message takes this form:

```text
error: Repository cache '<PATH>' is still dirty after cleanup.
```

Why: every reuse and every pack is followed by a hard reset and clean, as is the end of a run, including a run that ends in failure, and the run fails rather than build against a tree that is still dirty. Usual causes are a file inside the cache directory that another program holds open, a file the operating system refuses to delete, and a checkout that Git reports as modified immediately after a reset, for example because of line-ending normalization. [Repository cache](repository-cache.md) documents the exact sequence.

Fix: close whatever is using the directory, delete the cache directory named in the message, and rerun. The next run reclones it. Keep nothing of your own in a cache directory.

### The repository cache keeps growing

Symptom: disk usage under the cache root climbs, and uninstalling a source tool does not shrink it.

Why: `uninstall` removes the global tool and the installation record but retains the cached repository on purpose, so a later reinstall does not have to clone again. Directories left behind by repositories you no longer have installed stay until you remove them. Prune removes only direct children of `<CACHE_ROOT>/repositories` that no installation record uses. `cache list` omits size, while `cache show JKamsker/bookmeta-cli` computes it on demand.

Fix: inspect, preview, then prune.

```console
dotnet git-tool cache list
dotnet git-tool cache prune --dry-run
dotnet git-tool cache prune --yes
```

### `cache prune` cannot delete a directory

Symptom: exit `1`, kind `cache_prune_failed`, and a message of the form `Could not remove cached repository '<PATH>': <DETAIL>`.

Why: the delete threw an I/O or permission error. `dotnet-git-tool` clears the read-only attribute on every file first, which is what makes the read-only objects inside `.git` deletable on Windows, so a remaining failure comes from outside the tool. On Windows, the nested paths under `.git` can also grow long enough that deletion fails. Repositories skipped because another run holds their lock are reported separately and are not a failure.

Fix, in this order:

- Close anything holding a file in that directory: an editor, a running build, a file indexer.
- Exclude the cache root from real-time scanning if an on-access antivirus scanner is holding handles.
- Check the permissions on the directory.
- On Windows, move the cache root nearer the drive root with `DOTNET_GIT_TOOL_CACHE`, described in [Configuration](configuration.md).

### A private repository fails to clone, or the run stops partway

Symptom: either exit `1` with a `Cloning <SOURCE_ID> failed:` message, or a run that stops after a status line and never finishes:

```text
Preparing cached repository for JKamsker/bookmeta-cli...
```

Why: `dotnet-git-tool` runs `git` as an external command for all network access and has no credential handling of its own. Authentication comes from whatever credential helper, SSH agent, or Git configuration your user already has. The child process inherits your environment, but its output is captured rather than displayed, so a credential helper that wants to prompt writes into a buffer nobody shows and the run looks stuck.

The last status line printed names the phase that is hung. `--json` and `--quiet` suppress those lines, so a hung CI job shows nothing at all.

Fix:

- Make authentication work without a prompt first. Clone the repository by hand with the same URL `dotnet-git-tool` would use, and run `install` only after that succeeds silently. `owner/repo` always becomes `https://github.com/owner/repo.git`, and `--verbose` prints the resolved clone URL before the clone starts.
- To use SSH on GitHub, pass `git@github.com:JKamsker/bookmeta-cli.git`. That shorthand matches github.com only, and other hosts need a full URL with an explicit `ssh://` scheme.

Submodules are a related limitation: the clone does not pass `--recurse-submodules` and nothing initializes submodules afterwards, so a repository that needs submodule content to build fails at the pack step.

### A misspelled flag was ignored instead of rejected

Symptom: a flag you passed had no effect. For example `--dryrun --yes` performs a real installation.

Why: unknown options are discarded rather than rejected, so a typo silently disappears and the rest of the command line still runs.

Fix: confirm a preview by its output, not by the flag you typed. A real preview prints a line beginning with `Would prepare`, `Would refresh`, `Would uninstall`, or `Would remove`, or, for `cache prune --dry-run` with nothing to remove, `No unused repositories found in <CACHE_ROOT>/repositories.` Under `--json` it sets `data.action` to `install`, `update`, `uninstall`, or `cache_prune_preview`. Check spellings against the option tables in the [CLI reference](cli-reference.md).

### Every command fails with `Could not read state file` (`invalid_state`)

Symptom: exit `1` from commands that seem unrelated to state, including `list`, `cache list`, `cache show`, and `cache prune`, with a message of the form `Could not read state file '<PATH>': <DETAIL>`.

Why: the installation state file is unreadable JSON, or its `schemaVersion` is not `1`. A hand edit does that, and so does a truncated write or another program writing into the state directory. The `cache` commands read the installation records to decide which cached repositories are managed, so a broken state file takes that whole branch down with it.

Fix: the path is in the message. Repair the JSON, or move the file aside; a missing file is treated as no installations rather than an error. After moving it aside, reinstall each source tool to rebuild the records. [Configuration](configuration.md) documents the file shape.

## Error index

Exit codes and what each kind means are in the [CLI reference](cli-reference.md#error-kinds); this table adds the message text each kind produces and the remedy. A kind that emits more than one message lists them all.

| Kind | Message | First thing to try |
|---|---|---|
| `already_installed` | `'<SOURCE_ID>' is already managed. Use 'dotnet git-tool update <SOURCE_ID>'.` | Run `update` instead, or `uninstall` then `install`. |
| `ambiguous_cache_repository` | `Cached repository name '<SELECTOR>' is ambiguous. Use one of: <SOURCE_IDS>` | Rerun with one of the source IDs the message lists. |
| `ambiguous_project` | `Found multiple PackAsTool projects: <PATHS>. Pass --project <PATH>.`, `Found multiple executable projects: <PATHS>. Pass --project <PATH>.` | Pass `--project <PATH>`. |
| `cache_prune_failed` | `Could not remove cached repository '<PATH>': <DETAIL>` | Close programs holding files in that directory and rerun. |
| `cache_repository_not_found` | `Cached repository '<SELECTOR>' was not found.` | Run `cache list` and use a name from it. |
| `cancelled` | `Operation cancelled.` | Rerun and answer `y`, or pass `--yes`. |
| `child_process_failed` | `<OPERATION> failed: <LAST_OUTPUT_LINE>`, `<OPERATION> failed with exit code <N>.` | Read the operation prefix in the message, then reproduce that step by hand. |
| `confirmation_required` | `Building '<SOURCE_DISPLAY>' can execute arbitrary repository code. Inspect with --dry-run or explicitly consent with --yes.`, `Uninstalling '<SOURCE_ID>' requires confirmation. Inspect with --dry-run or confirm with --yes.`, `Removing <N> unused cached repositories requires confirmation. Inspect with --dry-run or explicitly confirm with --yes.` | Pass `-y`, or preview with `--dry-run`. |
| `default_branch_not_found` | `The remote repository did not report a default branch.` | Pass `--ref` with an explicit branch name. |
| `dependency_not_found` | `Could not start '<COMMAND>'. Make sure it is installed and available on PATH.` | Install `git` or `dotnet` and make sure it is on `PATH`. |
| `installation_not_found` | `'<SOURCE_ID>' is not managed. Install it first with 'dotnet git-tool install <REPOSITORY_AS_TYPED>'.`, `'<SOURCE_ID>' is not managed by dotnet git-tool.` | Run `list` and copy the recorded source ID. |
| `invalid_cache_prune_path` | `Refusing to prune path outside the repository cache: '<PATH>'.` | Report it; prune refuses to touch anything outside the cache root. |
| `invalid_cache_repository` | `A repository name is required.` | Not reachable through the command line; a blank `cache show` selector exits `1` instead. |
| `invalid_command_style` | `--standalone and --dotnet-command cannot be used together.` | Pass one of `--standalone` and `--dotnet-command`, or neither. |
| `invalid_manifest` | `Invalid .config/dotnet-git-tool.json: <DETAIL>` | Validate `.config/dotnet-git-tool.json` in the target repository. |
| `invalid_project` | `The selected project must be inside the cloned repository.`, `Directory '<PATH>' must contain exactly one .csproj file; found <N>.` | Use a repository-relative path to a single `.csproj` inside the cached repository. |
| `invalid_ref` | `The requested ref is not a valid branch, tag, or commit name.` | Use a plain branch or tag name. |
| `invalid_repository_cache` | `Cache path '<PATH>' exists but is not a Git repository. Move it aside and retry.` | Move the directory named in the message aside and rerun. |
| `invalid_source` | `Repository must be an owner/repo GitHub slug or an HTTP(S)/SSH Git URL.`, `Repository URLs must include an owner and repository path.` | Use `owner/repo`, or a full clone URL. |
| `invalid_state` | `Could not read state file '<PATH>': <DETAIL>` | Repair or move aside the file named in the message. |
| `invalid_tool_command` | `Discovered command '<COMMAND>' cannot be exposed as a .NET tool command.` | Set `ToolCommandName`, or a manifest `command`, to a name starting with a letter or digit. |
| `project_evaluation_failed` | `MSBuild returned an unreadable project evaluation for '<PATH>': <DETAIL>` | Run `dotnet msbuild <PATH> -getProperty:OutputType` in your own clone. |
| `project_not_executable` | `Selected project '<PATH>' is not executable. Expected OutputType=Exe or PackAsTool=true.` | Point `--project` at the console project. |
| `project_not_found` | `No .csproj files were found in the repository.`, `No executable project was found. Pass --project to select a project explicitly.`, `Project '<PATH>' was not found.` | Pass `--project`, or check the repository has a C# executable. |
| `repository_cache_conflict` | `Cache path '<PATH>' belongs to a different remote ('<ORIGIN>'). Move it aside and retry.` | Move the directory named in the message aside and rerun. |
| `repository_cache_dirty` | `Repository cache '<PATH>' is still dirty after cleanup.` | Delete the cache directory named in the message and rerun. |
| `repository_cache_locked` | `Timed out waiting for another operation using this repository cache.` | Wait for the other run to finish, then rerun. |
| `state_locked` | `Timed out waiting for another dotnet git-tool operation to finish.` | Wait for the other run to finish, then rerun. |
| `unexpected_error` | The runtime exception's own message, so there is no fixed text. | Open an issue with the full command, the exit code, and the `--json` envelope. |

### Failures that carry no error kind

The argument parser and the per-command argument validation both run before a command body starts, so they produce no error kind and no envelope. They write one plain `error: <MESSAGE>` line to stderr, leave stdout empty even under `--json`, and exit `1`. An unknown command such as `dotnet git-tool bogus` behaves this way, as does a missing required argument and a blank `cache show` selector.

Passing `--standalone` together with `--dotnet-command` is checked inside the command body instead, so that one does produce `invalid_command_style`, an envelope, and exit `2`.

A ref that starts with `-` reaches validation in some spellings and not others. The space-separated `--ref -x` is consumed by the argument parser first, so it exits `1` with a plain `error:` line. Written `--ref=-x` or as the embedded `JKamsker/bookmeta-cli@-x`, the same value reaches `invalid_ref` and exit `2`.

Two more cases are not failures. Bare `dotnet git-tool` prints the root help on stdout and exits `0`. `dotnet git-tool cache` with no subcommand prints the branch help on stdout and writes nothing to stderr, but still exits `1`.

## Getting more detail

### `--verbose`

`--verbose` adds up to five diagnostic lines on stderr, in human output only. `--json` and `--quiet` suppress them.

```console
dotnet git-tool install JKamsker/bookmeta-cli --yes --verbose
```

The diagnostic lines, interleaved with the usual status output on stderr:

```text
Clone URL: https://github.com/JKamsker/bookmeta-cli.git
Repository cache: /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
Resolved commit: 4fbe47e663597fb0da63f344373cfeeee99c6a26
Selected project: src/BookMeta.Cli/BookMeta.Cli.csproj
Generated package: git.JKamsker.bookmeta-cli 0.0.0-git.4fbe47e66359.dotnet; tool command: dotnet-bookmeta; invocation: dotnet bookmeta
```

They tell you which repository, commit, project, and package a run resolved to. They add nothing from `git` or MSBuild, so `--verbose` does not surface a failing build log. Only `install` and `update` emit them; `uninstall`, `list`, and the `cache` commands emit none, so `--verbose` changes nothing there.

### `--json`

`--json` prints the envelope on stdout and keeps stderr empty, so `error.kind` is the reliable way to branch on a failure.

```console
dotnet git-tool install JKamsker/bookmeta-cli --json
```

Output, at exit `2`:

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

`System.Text.Json` web defaults escape the apostrophe as `\u0027`, so leave that escape alone when you compare message text. The exception to the envelope guarantee is the parse-time failures above: stdout is empty, stderr carries one plain line, and the exit code is `1`. Treat empty stdout plus a non-zero exit as a valid outcome. The full envelope shape belongs to the [CLI reference](cli-reference.md); scripting patterns belong to [Automation](automation.md).

### Reproducing a build outside the tool

When a run fails during `Evaluating` or `Packing`, the fastest path to the real error is to run the build yourself. `dotnet git-tool cache show <REPOSITORY>` prints the cache directory and the resolved commit; its `Project` line is filled in only for a repository that is already managed, so after a failed first install take the project path from the `Evaluating` or `Packing` message instead. Running `dotnet pack <PATH> --configuration Release -p:PackAsTool=true` there shows the full build output instead of the single line the tool reports, and `-v detailed` adds the complete MSBuild log. Copy the directory first if you want the state to survive the next run.

### What to include when you open an issue

Report problems on the [issue tracker](https://github.com/JKamsker/dotnet-git-tool/issues) with all of the following:

- The exact command you ran, with `--verbose` added, and the complete stderr from that run.
- The `--json` envelope, when the failure produces one.
- Your operating system and version, plus the output of `dotnet git-tool --version`, `dotnet --list-sdks`, and `git --version`.
- Whether the target repository builds on its own: clone it yourself, run `dotnet pack <PATH> --configuration Release -p:PackAsTool=true`, and say whether that succeeds.

Remove credentials and private URLs from anything you paste.

## See also

- [Documentation index](README.md)
- [CLI reference](cli-reference.md)
- [Repository cache](repository-cache.md)
- [Configuration](configuration.md)
- [Automation](automation.md)
