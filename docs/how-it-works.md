# How it works

`dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`) turns a Git repository into an installed .NET global tool. This page follows a single `install` from the argument you type to the installed global tool you can run, and explains why each step exists.

Paths in the examples use the Linux default cache root `/home/you/.cache/dotnet-git-tool`. See [Configuration](configuration.md) for the per-platform defaults.

## What `dotnet tool install` cannot do

`dotnet tool install --global <PACKAGE_ID>` needs a package that already exists on a NuGet feed. When the author of a tool never published one, there is nothing for that command to install.

`dotnet-git-tool` produces the package on your machine instead. It clones the repository, finds the executable project, runs `dotnet pack` on it, and hands the result to `dotnet tool install --global` from a temporary local feed. That last step is the same `dotnet tool install` you would run yourself, so what you end up with is an ordinary .NET global tool. Everything `dotnet-git-tool` adds happens before the package exists: deciding which repository and which commit, caching the sources, picking the project, and deriving a package identity from the repository rather than from the author's release process.

## The install pipeline

```text
dotnet git-tool install JKamsker/bookmeta-cli --yes
   |
   v
Stage 1  parse the repository argument
   |
   +-- already managed? ----------------> already_installed, exit 6
   |
   +-- --dry-run? ---------------------> print the preview, exit 0
   |
   +-- confirmation, unless --yes
   |
   v
Stage 2  lock the cache directory, then clone, or clean and fetch
   |
   v
Stage 3  discover and evaluate the project with MSBuild
   |
   v
Stage 4  derive the package ID, the version, and the command name
   |
   v
Stage 5  dotnet pack into a temporary directory
         clean the cached repository
         dotnet tool install --global
   |
   v
Stage 6  write the installation record
   |
   +-- write fails -----> dotnet tool uninstall --global, best effort
   |                      |
   |                      v
   |                      the original error is rethrown, the command fails
   |
   +-- write succeeds --> clean again, release the lock, exit 0
```

The six stages are:

1. **Source resolution.** Turn the repository argument and the command flags into a clone URL, a source ID, and an optional requested ref.
2. **The cached repository.** Take a per-repository lock, clone or refresh a shallow working tree in the repository cache, and resolve the exact commit.
3. **Project discovery.** Find the `.csproj` to build and read its properties through MSBuild.
4. **Identity generation.** Derive the generated package ID, the version string, and the command name.
5. **Packing and global install.** Run `dotnet pack` into a temporary directory, then `dotnet tool install --global` against that directory as a NuGet feed.
6. **Recording state.** Write an installation record so `list`, `update`, `uninstall`, and `cache prune` know the tool exists.

Error kinds and exit codes named on this page are defined in the [CLI reference](cli-reference.md#error-kinds), and the fix for each one is in [Troubleshooting](troubleshooting.md).

A preview returns at the end of stage 1 without taking the lock, cloning, or running repository code. See [Security](security.md) for what executes and when.

A preview is not a validation. It reports what stage 1 resolved from local state and nothing more, so it does not check that the repository exists, that the ref exists, or that a `--project` path is inside the cached repository. The one state it does check is the installation record: `install --dry-run` on a source that is already managed fails with `already_installed` before it reaches the preview, and `update --dry-run` and `uninstall --dry-run` fail with `installation_not_found` when there is no record.

The preview line names the stages it would run:

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
```

Output:

```text
Would prepare cached sources for JKamsker/bookmeta-cli, discover a tool project, pack it for a 'dotnet <command>' invocation, install it globally, and retain clean sources at /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac.
```

## Stage 1: source resolution

Parsing the repository argument produces three values: the **clone URL** that `git` is given, the **source ID** that identifies the repository everywhere else, and the **requested ref** if you pinned one. For `JKamsker/bookmeta-cli@v1.2.0` those are `https://github.com/JKamsker/bookmeta-cli.git`, `JKamsker/bookmeta-cli`, and `v1.2.0`.

The source ID is the load-bearing value. It determines the cache directory name (stage 2), the generated package ID (stage 4), and the key under which the installation is recorded (stage 6). Two arguments that normalize to the same source ID address the same installation. The accepted input shapes and their validation rules belong to the [CLI reference](cli-reference.md).

### Resolution precedence for install

An install has no prior state to inherit from, so each value comes from the first source that supplies it:

1. **Ref.** `--ref <REF>` wins over an `@ref` embedded in the argument. With neither, no ref is requested and stage 2 resolves the remote default branch.
2. **Project.** `-p, --project <PATH>` wins over the manifest's `project` field in `.config/dotnet-git-tool.json`. With neither, stage 3 ranks every candidate project. This is not a .NET tool manifest (`dotnet-tools.json`); `dotnet-git-tool` does not read or write those.
3. **Command name.** The manifest's `command` field wins over the project's `ToolCommandName`, which wins over its `AssemblyName`. The manifest's `command` applies even when you pass `--project`, because `--project` overrides only the manifest's `project` field.
4. **Command style.** `--standalone` or `--dotnet-command` wins. With neither, an install defaults to dotnet style. Passing both is `invalid_command_style`.

### Resolution precedence for update

An update reads the installation record first and treats it as the baseline. What you type on the command line overrides the record; nothing else does:

1. **Ref.** `--ref <REF>` wins over an `@ref` embedded in the argument. With neither, the recorded pin is cleared and stage 2 resolves the remote default branch.
2. **Project.** `--project <PATH>` overrides the recorded project. Otherwise the recorded project is reused, which reduces stage 3 to a single evaluation.
3. **Command style.** `--standalone` or `--dotnet-command` overrides the recorded style. Otherwise the recorded style is reused. A record written without a style is inferred as dotnet style only when its recorded command begins with `dotnet ` or `dotnet-`, and as standalone style otherwise.
4. **Clone URL and package ID.** Both come from the record, never from the argument. Passing a different URL that normalizes to the same source ID does not repoint the remote.

An update without a ref deliberately returns to the remote default branch. After `install JKamsker/bookmeta-cli@v1.2.0`, a later `update JKamsker/bookmeta-cli` fetches the latest default-branch commit, rebuilds it, and clears the recorded pin. Repeat the ref when the update should remain pinned.

## Stage 2: the cached repository

Every source ID maps to one deterministic directory under `<CACHE_ROOT>/repositories`, named from the source ID plus a hash of it: `JKamsker/bookmeta-cli` becomes `JKamsker-bookmeta-cli-1cd22d4b86ac`. [Repository cache](repository-cache.md#cache-directory-names) gives the naming rule. Because the name is derived rather than allocated, the same repository always lands in the same place and `dotnet-git-tool` can compute the path without touching the disk, which is what a preview reports.

Before anything touches that directory, `dotnet-git-tool` takes an exclusive repository lock, which is what keeps two runs against the same repository from colliding.

A first install clones shallowly:

```console
git clone --depth 1 --no-tags https://github.com/JKamsker/bookmeta-cli.git /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

If you requested a ref, `dotnet-git-tool` then fetches it by name at depth 1 and checks it out at `FETCH_HEAD` without creating a branch, which is why `cache show` reports `detached` in its `Branch` field for a repository installed with a ref. That second fetch is why branches and tags resolve even though `git clone` ran shallow and without tags. An arbitrary old commit SHA that the remote will not serve at depth 1 fails.

When the directory already exists, `dotnet-git-tool` validates that it is a Git repository whose `origin` matches the clone URL, cleans it, points `origin` at the clone URL again, and then either checks out the requested ref or resolves the remote default branch with `git ls-remote --symref origin HEAD` and fetches and checks that branch out. Both paths move the working tree onto the fetched commit with a checkout: `git checkout --detach FETCH_HEAD` for a requested ref, `git checkout -B <BRANCH> refs/remotes/origin/<BRANCH>` for the default branch. There is never a merge and never a rebase, so a cached repository cannot accumulate local history that diverges from the remote.

Reuse still contacts the remote. Both `install` and `update` fetch on every real run, so neither works offline.

Finally `git rev-parse HEAD` resolves the commit, and that value flows into the version string in stage 4 and the installation record in stage 6.

The clean guarantee is that `dotnet-git-tool` resets and cleans the cached repository every time it reuses an existing one, again after a successful pack, and again when it releases the cache handle, which includes the failure path. A clean that leaves anything behind fails with `repository_cache_dirty` rather than proceeding. Submodules are not initialized: `dotnet-git-tool` does not clone them recursively and nothing runs `git submodule update`, so a repository that needs submodule content fails once that content is required, during project evaluation or during the build. [Repository cache](repository-cache.md) covers the layout, the lock, and the clean guarantee in full.

## Stage 3: project discovery

The project to build is chosen by the first rule that resolves:

1. `-p, --project <PATH>`, a project file or a directory, resolved against the cached repository root.
2. The manifest's `project` field in `.config/dotnet-git-tool.json`.
3. Exactly one project with `PackAsTool=true`.
4. Exactly one project with `OutputType=Exe`.

Rules 1 and 2 are an explicit selection, and each of their constraints has its own failure. A path that leaves the cached repository, or a directory that does not hold exactly one `.csproj` at its top level, fails with `invalid_project`. A path that does not exist, or that is not a `.csproj`, fails with `project_not_found`. A project that is neither `OutputType=Exe` nor `PackAsTool=true` fails with `project_not_executable`.

When nothing is selected explicitly, the candidate set is every `*.csproj` found recursively under the cached repository root, skipping any path whose segments include `.git`, `bin`, or `obj`, sorted ordinal-ignore-case. Only `.csproj` is enumerated. F# and Visual Basic projects are not candidates.

`dotnet-git-tool` evaluates each candidate with one MSBuild invocation, run with the cached repository as the working directory and with the project path passed absolute:

```console
dotnet msbuild /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac/src/BookMeta.Cli/BookMeta.Cli.csproj --nologo -getProperty:OutputType,PackAsTool,AssemblyName,ToolCommandName
```

Every candidate goes through this because the ranking is defined over property values, and those values are not visible in the project file alone. `PackAsTool`, `OutputType`, `AssemblyName`, and `ToolCommandName` can come from a `Directory.Build.props` several directories up, from an imported `.targets` file, or from a condition. Only MSBuild can compute what they actually are, so there is no shortcut based on file names or a text scan.

The cost is one `dotnet msbuild` process per candidate, and the evaluations run one after another. A repository with 30 project files runs 30 evaluations before packing starts. Passing `--project`, or shipping a manifest with a `project` field, reduces that to one. Evaluation reads properties rather than compiling, but it still runs the repository's own MSBuild logic, which is why [Security](security.md) counts it as code execution.

Ambiguity is an error rather than a guess. Two or more `PackAsTool` projects, or zero `PackAsTool` projects and two or more `Exe` projects, fail with `ambiguous_project` and a message listing the candidates and telling you to pass `--project <PATH>`. Zero of both fails with `project_not_found`. [Authoring tools](authoring-tools.md) covers how to make a repository unambiguous from the author's side.

## Stage 4: identity generation

### Generated package ID

`dotnet-git-tool` builds the generated package ID from the source ID alone. It replaces `/` with `.`, replaces every run of characters outside `[A-Za-z0-9_.-]` with a single `-`, trims leading and trailing `.` and `-`, and prefixes `git.`. `JKamsker/bookmeta-cli` becomes `git.JKamsker.bookmeta-cli`.

The `git.` prefix keeps generated packages visibly distinct from anything published to a real feed. If the derived ID would exceed 100 characters, `dotnet-git-tool` cuts it to its first 87 characters and appends a `.` and the first 12 lowercase hex characters of the SHA-256 of the source ID, which lands on exactly 100 characters and keeps very long source IDs distinguishable.

### Generated version

The general form is:

```text
0.0.0-git.<COMMIT>.<STYLE>
```

`<COMMIT>` is the first 12 characters of the resolved commit, lowercased, and `<STYLE>` is `dotnet` or `standalone`. For the example repository at commit `4fbe47e663597fb0da63f344373cfeeee99c6a26` in dotnet style that is `0.0.0-git.4fbe47e66359.dotnet`.

The release part stays `0.0.0` forever. All identity lives in the prerelease label, which keeps a generated package version visibly distinct from a version a published release would carry. Your project's own `<Version>` does not reach the package, because stage 5 passes `-p:Version=` with the generated string, which overrides it. `cache list` and `cache show` still read your project's version live from the checked-out project and show it as the source version; see [Repository cache](repository-cache.md).

The command style is part of the version for two reasons. First, the two styles produce genuinely different packages from the same commit, because the packaged command name is baked in at pack time, so they need different versions. Second, it gives `update` a precise definition of "nothing to do": an update reports `unchanged` only when the resolved commit and the command style both match the record, and a style change alone forces a full repack at the same commit.

A side effect is that these version strings do not increase monotonically. Prerelease labels compare segment by segment, and the commit segment is a hash rather than a counter, so a newer commit frequently sorts below the installed version rather than above it. That is why the update path in stage 5 passes `--allow-downgrade`.

### Command name and style

The discovered command name comes from the manifest's `command` field, then the project's `ToolCommandName`, then the project's `AssemblyName`. A blank `ToolCommandName` counts as a missing one, which is what lets an unmodified console app be installed without any change to the repository. [Authoring tools](authoring-tools.md#command-names) gives the full rule and the pattern a base name has to match.

`dotnet-git-tool` then normalizes the discovered name. It strips a leading `dotnet-` to get the **base name**, so a project already named `dotnet-bookmeta` and a project named `bookmeta` both yield the base name `bookmeta`. A base name that fails the name pattern fails the run with `invalid_tool_command`.

The style then decides the two names the user sees:

| Command style | Packaged command name | Invocation |
|---|---|---|
| dotnet | `dotnet-bookmeta` | `dotnet bookmeta` |
| standalone | `bookmeta` | `bookmeta` |

`dotnet-git-tool` does not make the packaged command name unique across repositories. Two repositories whose projects resolve to the same base name produce two different generated package IDs but one packaged command name. `dotnet-git-tool` neither detects that nor renames either of them, so the outcome is whatever `dotnet tool install --global` does with two packages claiming one command name. Installing one of them in the other command style changes its packaged command name, `bookmeta` rather than `dotnet-bookmeta`, which removes the clash; [Authoring tools](authoring-tools.md#command-names) covers choosing a base name that is unlikely to collide.

## Stage 5: packing and the global install

Packing runs from the cached repository as its working directory and, like project evaluation, takes the project path absolute. `<PACKAGE_DIR>` stands for a fresh temporary directory:

```console
dotnet pack /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac/src/BookMeta.Cli/BookMeta.Cli.csproj --configuration Release --output <PACKAGE_DIR> -p:PackAsTool=true -p:PackageId=git.JKamsker.bookmeta-cli -p:Version=0.0.0-git.4fbe47e66359.dotnet -p:ToolCommandName=dotnet-bookmeta
```

`-p:PackAsTool=true` is always passed, so a plain console application packs as a tool without the author setting anything. `<PACKAGE_DIR>` is `packages` inside a directory named `dotnet-git-tool-package-<GUID>` under the system temporary directory, and `dotnet-git-tool` deletes it when the operation finishes, including when packing throws. Deletion is best effort: if a file in it is locked, the directory is left behind for your operating system's temporary-file cleanup.

Packing outside the cache keeps the `.nupkg` out of the cached repository. The build still writes `bin` and `obj` under the project, and the clean that runs immediately after a successful pack removes them, which is what makes the cached repository hold only tracked source files.

The temporary directory is then handed to the .NET SDK as a NuGet feed:

```console
dotnet tool install --global git.JKamsker.bookmeta-cli --version 0.0.0-git.4fbe47e66359.dotnet --add-source <PACKAGE_DIR> --ignore-failed-sources
```

`update` runs the same command with `update` in place of `install` and one extra flag:

```console
dotnet tool update --global git.JKamsker.bookmeta-cli --version 0.0.0-git.4fbe47e66359.dotnet --add-source <PACKAGE_DIR> --ignore-failed-sources --allow-downgrade
```

Each flag is doing specific work. `--add-source` makes the freshly packed `.nupkg` findable without changing your NuGet configuration. `--version` pins the exact generated version rather than letting NuGet resolve a range. `--ignore-failed-sources` is passed because your configured feeds are still consulted alongside the temporary one, so a feed that fails does not fail the install. `--allow-downgrade` is required on update because generated versions do not sort monotonically, as described in stage 4.

`dotnet-git-tool` captures `git` and `dotnet` output rather than streaming it, so a long clone or a multi-minute build prints nothing while it runs. When one of them exits non-zero the failure is reported as `child_process_failed` carrying the last non-empty line of the child's standard error, or of its standard output when standard error was empty. When the child printed nothing at all, the message reports only the exit code. [Troubleshooting](troubleshooting.md) covers how to see the real build error.

## Stage 6: recording state

After the global install succeeds, `dotnet-git-tool` appends an installation record to the installation state file. The record preserves the installed tool's identity and build choices for later updates:

- `sourceId` is the key, `cloneUrl` identifies the remote, and `requestedRef` reports whether the last operation selected a pin or the default branch.
- `project` lets `update` rebuild the same project, which is what makes a repository with several executables update deterministically after one `--project` choice.
- `packageId` is reused verbatim, so the global tool keeps the identity it was installed under.
- `commit` and `commandStyle` are what `update` compares against to decide between `unchanged` and a full repack.
- `command` records the invocation, and `repositoryPath` is what `list` prints and what `cache prune` treats as in use.
- `installedAt` and `updatedAt` are timestamps. `updatedAt` stays null until an update actually changes the package.

An `update` that finds the same commit and the same style still rewrites the record, refreshing `commandStyle` and `repositoryPath`, and deliberately leaves `updatedAt` alone. A record whose `repositoryPath` is stale or null is repaired by running `update`.

The exact key names and types belong to the [CLI reference](cli-reference.md); where the file lives belongs to [Configuration](configuration.md).

### Rollback when the record cannot be written

Writing the record can fail on its own: the state lock can time out after 10 seconds, or an `installed.json` with an unreadable shape can fail to load. At that point the global tool is already installed, so leaving it there would produce a tool that `list`, `update`, and `uninstall` know nothing about.

Install handles this by rolling back: if writing the record throws, `dotnet-git-tool` runs `dotnet tool uninstall --global <PACKAGE_ID>` and then rethrows the original error. The rollback is best effort, because its result is not checked, so an uninstall that itself fails leaves exactly the half-installed state the rollback exists to prevent. Running `dotnet tool uninstall --global git.JKamsker.bookmeta-cli` removes such a tool by hand. The cached repository created during the run is retained either way, as it is after any successful install.

Update has no equivalent rollback. If the record cannot be replaced after `dotnet tool update` succeeded, the newer package stays installed while the record still describes the previous commit. Running `update` again reconciles it.

## Uninstall

`uninstall` runs almost none of this pipeline. It normalizes the repository argument to a source ID, looks up the installation record, asks for confirmation, runs one command, and removes the record:

```console
dotnet tool uninstall --global git.JKamsker.bookmeta-cli
```

It takes no repository lock, contacts no remote, evaluates no project, and packs nothing. It also leaves the cached repository in place, which is why its success line ends with `Cached sources retained at` and the cache directory path. [Repository cache](repository-cache.md) covers reclaiming that space with `cache prune`.

## The SDK fallback

Project evaluation (stage 3) and packing (stage 5) run inside the cached repository, so the repository's own `global.json` governs which SDK the `dotnet` driver selects. When an author pins an SDK you do not have, that resolution fails.

The SDK fallback is a single, narrowly triggered retry. It engages only when a `dotnet` invocation for evaluation or packing failed **and** its standard output or standard error contains the string `https://aka.ms/dotnet/sdk-not-found`. Then:

1. Read `global.json` from the cached repository root and take `sdk.version`. A missing file, a missing property, or unparsable JSON ends the fallback and the original failure is returned.
2. Parse that value as a numeric version, dropping any prerelease suffix after the first `-`.
3. Run `dotnet --list-sdks` and parse each reported version the same way. At least one installed SDK must be strictly newer than the pinned one. If none is, the original failure is returned.
4. Create a fresh temporary directory and re-run the identical command from there. The project path in the arguments is absolute, so the build still targets the cached repository while SDK resolution no longer sees the repository's `global.json`.
5. Delete the temporary directory, and return whatever the retry produced, success or failure.

What the fallback deliberately does not do matters as much as what it does:

- It does not install an SDK, and it does not download anything.
- It does not retry with an older SDK. The installed SDK must be strictly newer, so an author pinning an SDK from the future still fails.
- It does not retry ordinary failures. Restore errors, compile errors, and pack errors do not contain that help URL and are returned unchanged on the first attempt.
- It does not modify, delete, or rewrite the repository's `global.json`. Only the working directory of the retry changes, and the cached repository is left untouched.
- It does not read `rollForward` or any other `global.json` setting. Only `sdk.version` is consulted.
- It does not apply to `dotnet tool install`, `dotnet tool update`, or `dotnet tool uninstall`, which never run inside the repository.
- It retries once. There is no loop and no second fallback.

## See also

- [Documentation index](README.md)
- [CLI reference](cli-reference.md)
- [Repository cache](repository-cache.md)
- [Authoring tools](authoring-tools.md)
- [Security](security.md)
