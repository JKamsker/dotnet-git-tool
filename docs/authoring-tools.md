# Authoring a tool repository

This page is for you if you own a repository and want `dotnet git-tool install JKamsker/bookmeta-cli` to work
for the people who read your README. `dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as
`dotnet git-tool`) clones your repository on your user's machine, finds one project in it, packs that project
as a .NET global tool, and installs it. Your side of that contract is small: one buildable C# console project,
and a predictable answer to the question "which project, and what is the command called".

Every example on this page uses the repository `JKamsker/bookmeta-cli` and the base name `bookmeta`. Read them
as your own `owner/repo` and your own base name throughout, including in the commands you run to verify your
repository and in the block you publish in your own README.

Paths in the examples use the Linux defaults. macOS uses the same `~/.cache/dotnet-git-tool` layout, not
`~/Library/Caches`; Windows uses `C:\Users\You\AppData\Local\dotnet-git-tool\cache`. See
[Configuration](configuration.md).

## The minimum viable repository

One `.csproj` with `OutputType=Exe` is enough. You do not need `PackAsTool`, a manifest, or any other change.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>bookmeta</AssemblyName>
  </PropertyGroup>

</Project>
```

`dotnet-git-tool` passes `-p:PackAsTool=true` on its own `dotnet pack` command line, so an ordinary console
project packs as a global tool without declaring it. With the project above, the command name comes from
`AssemblyName`, so your users run `dotnet bookmeta`. This holds only while the repository contains exactly one
executable project, which is rarer than it sounds.

If you set neither `AssemblyName` nor `ToolCommandName`, the command name is the project file name, because
that is MSBuild's default `AssemblyName`. A project file called `BookMeta.Cli.csproj` therefore installs as
`dotnet BookMeta.Cli`, and nothing errors, because a dot is legal in a command name. Set one of the two
properties unless that is the command you want.

Two limits apply to every repository:

- Only `.csproj` projects are discovered. `.fsproj` and `.vbproj` projects are not supported.
- Your project has to build in the `Release` configuration, because packing runs
  `dotnet pack --configuration Release`.

## Recommended project setup

Set `PackAsTool` and `ToolCommandName` explicitly. Marking exactly one project `PackAsTool=true` makes it win
project discovery outright, ahead of every other executable in the repository (see
[How your project gets picked](#how-your-project-gets-picked)); marking two is an error. `ToolCommandName`
pins the command name so it does not follow `AssemblyName` the day you rename the assembly.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>bookmeta</AssemblyName>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>bookmeta</ToolCommandName>
  </PropertyGroup>

</Project>
```

Both properties keep working for people who install your tool the ordinary way with `dotnet tool install`, so
adding them costs you nothing.

## How your project gets picked

Project discovery applies these rules in order. The first rule that applies decides the outcome: if it matches
exactly one project that project is used, and if it matches more than one the command fails rather than moving
to the next rule.

1. The `-p, --project <PATH>` option, if your user passes one.
2. The manifest's `project` field, if you commit `.config/dotnet-git-tool.json`.
3. The single project with `PackAsTool=true`, if exactly one exists.
4. The single project with `OutputType=Exe`, if exactly one exists.

When rules 1 and 2 do not apply, candidates are every `*.csproj` under the repository root, skipping any path
segment named `.git`, `bin`, or `obj`. Each candidate is evaluated with a separate `dotnet msbuild` run that
reads `OutputType`, `PackAsTool`, `AssemblyName`, and `ToolCommandName`. The mechanism is described in
[How it works](how-it-works.md).

## Multi-project repositories

A repository with class libraries and one console app resolves cleanly, because a library is not
`OutputType=Exe` and rule 4 finds a single candidate. Discovery breaks down as soon as a second executable
appears, and more repositories have one than you would expect. A sample app and a benchmark harness are
executables, and so is a modern test project: the `dotnet-git-tool` test project sets no `OutputType` at all
and still evaluates to `Exe`, because xunit v3 and other Microsoft.Testing.Platform runners build the test
project as a program. Check your own test projects before assuming your repository has one executable.

`dotnet-git-tool` never guesses between them. It fails with kind `ambiguous_project` and a message listing the
projects it could not choose between, either
`Found multiple PackAsTool projects: <paths>. Pass --project <PATH>.` or
`Found multiple executable projects: <paths>. Pass --project <PATH>.`

A repository that has `.csproj` files but no executable project fails with kind `project_not_found` and
`No executable project was found. Pass --project to select a project explicitly.` A repository with no
`.csproj` at all fails with the same kind and the message `No .csproj files were found in the repository.`

You have two fixes, and either one is enough:

- Mark exactly one project `PackAsTool=true`. Rule 3 then beats every other executable in the repository.
- Commit `.config/dotnet-git-tool.json`. Rule 2 then names the project directly, and only that one project is
  evaluated with MSBuild.

Do not leave the fix to your users. Without one, every install of your repository needs `--project`.

## The repository manifest

`.config/dotnet-git-tool.json`, at the root of your repository, is the file you commit to declare which project
is the tool and what the command is called. This is not a .NET tool manifest (`dotnet-tools.json`);
`dotnet-git-tool` does not read or write those.

```json
{
  "project": "src/BookMeta.Cli/BookMeta.Cli.csproj",
  "command": "bookmeta"
}
```

| Key | Type | Meaning |
|---|---|---|
| `project` | `string or null` | Path to the project, relative to the repository root. A directory works if it directly contains exactly one `.csproj`; it is not searched recursively. |
| `command` | `string or null` | The command name, before any style prefix. A leading `dotnet-` is stripped from it, the same as for `ToolCommandName`. Overrides `ToolCommandName` and `AssemblyName`. |

Both fields are optional, so a manifest with only `command` is valid. Binding rules worth knowing before you
commit the file:

- Only `<repository root>/.config/dotnet-git-tool.json` is read. A `.config` directory anywhere else in the
  repository is ignored, with no error.
- Key matching is case insensitive, so `project` and `Project` both bind, and unknown properties are ignored.
- Invalid JSON, or a file whose entire content is `null`, fails with kind `invalid_manifest`.
- `project` must resolve to a path inside your repository, otherwise the install fails with
  `The selected project must be inside the cloned repository.`
- The project you name must be `OutputType=Exe` or `PackAsTool=true`. Naming a class library fails with kind
  `project_not_executable` and
  `Selected project '<path>' is not executable. Expected OutputType=Exe or PackAsTool=true.`
- A path that does not exist fails with kind `project_not_found` and `Project '<value>' was not found.`, so
  update the manifest when you move the project.
- A directory holding any number of `.csproj` files other than one fails with kind `invalid_project` and
  `Directory '<value>' must contain exactly one .csproj file; found <n>.`
- `--project` on the command line overrides your `project` field, but your `command` field still applies.

## Command names

The command name is resolved in this order: the manifest's `command` field, then the project's
`ToolCommandName` if it is not empty, then the project's `AssemblyName`. MSBuild reports an unset property as
an empty string, so a project without `ToolCommandName` normally takes the `AssemblyName` branch.

A leading `dotnet-` is then stripped, case insensitively, to produce the base name, and the base name must
match `^[A-Za-z0-9][A-Za-z0-9_.-]*$`. A name that fails the pattern fails the install with kind
`invalid_tool_command` and the message
`Discovered command 'bookmeta cli' cannot be exposed as a .NET tool command.` Pick a base name with no spaces,
no slashes, and no leading punctuation.

Your users pick the command style, and it changes what they type:

- dotnet style, the default: the package exposes `dotnet-bookmeta`, and your users run `dotnet bookmeta`.
- standalone style, with `--standalone`: the package exposes `bookmeta`, and your users run `bookmeta`.

Because the `dotnet-` prefix is stripped first, a `ToolCommandName` of `dotnet-bookmeta` installs as
`dotnet bookmeta` in dotnet style and as `bookmeta` in standalone style. The prefix is never doubled.

What lands in the global tools directory on your user's machine is the packaged command name:
`dotnet-bookmeta` in dotnet style, `bookmeta` in standalone style. If a user already has a tool exposing that
name, `dotnet tool install` fails and they see one line with kind `child_process_failed`. Pick a base name
distinctive enough that neither form collides.

## Verifying your repository installs

Run these against your own repository, in order. You install from a remote, not from a local directory. There
is no local-path form, so push the branch or tag you want to test first. A relative path such as
`./bookmeta-cli` is not rejected: it parses as the `owner/repo` form, previews cleanly, and only fails once
`git clone` runs.

Start from a machine where your tool is not already installed. `install` refuses a source it already manages
with kind `already_installed` and exit code `6`, and `--dry-run` does not exempt you from that check. If it is
installed, run `dotnet git-tool uninstall JKamsker/bookmeta-cli --yes` first.

To keep the loop off your real machine state, point `DOTNET_GIT_TOOL_CACHE` and `DOTNET_GIT_TOOL_HOME` at a
scratch directory for the duration; both are described in [Configuration](configuration.md).

Preview first. This resolves the source ID and the cache directory without cloning anything:

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
```

Output:

```text
Would prepare cached sources for JKamsker/bookmeta-cli, discover a tool project, pack it for a 'dotnet <command>' invocation, install it globally, and retain clean sources at /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac.
```

The preview reads local state only. It does not verify that the repository exists, that the ref exists, or that
your project is discoverable, so it is a syntax check and not a validation of your repository.

Now do the real install and watch the diagnostics. `--verbose` adds five lines on stderr, labeled `Clone URL`,
`Repository cache`, `Resolved commit`, `Selected project`, and `Generated package`. `Selected project` and
`Generated package` are the two that tell you discovery picked what you intended:

```console
dotnet git-tool install JKamsker/bookmeta-cli --yes --verbose
```

Then confirm the command exists, is on `PATH`, and runs:

```console
dotnet bookmeta
```

`list` shows the row your users end up with:

```console
dotnet git-tool list
```

Output:

```text
SOURCE                         PACKAGE                            COMMIT         COMMAND                  CACHE PATH
JKamsker/bookmeta-cli          git.JKamsker.bookmeta-cli          4fbe47e663597… dotnet bookmeta          /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac
```

Check three things in that row: the generated package ID, the resolved commit, and the invocation your README
will tell people to type.

Test the pinned form your README will advertise. The source is managed now, so move the existing installation
with `update` rather than installing twice:

```console
dotnet git-tool update JKamsker/bookmeta-cli --ref v1.2.0 --yes
```

`JKamsker/bookmeta-cli@v1.2.0` is equivalent to `--ref v1.2.0`. The ref is passed through to `git` unchanged,
so a branch name or a commit SHA parses too; tags and branches resolve under the depth-1 fetch, while a bare
commit SHA depends on what the remote will serve, as the table below records. A pinned ref is recorded and
inherited by every later `update`, so once you have tested a tag, clear the pin by uninstalling and installing
again.

Finish by removing what you installed:

```console
dotnet git-tool uninstall JKamsker/bookmeta-cli --yes
```

`uninstall` keeps the cached repository on purpose. See [Repository cache](repository-cache.md) for removing
it.

## Things that will trip your users

| Situation | What your users hit | What to do |
|---|---|---|
| A user pins a raw commit SHA | Cloning uses `git clone --depth 1 --no-tags` and the pin is fetched with `git fetch --depth 1 origin <REF>`, which can fail for a commit that is not reachable at depth 1, see [Repository cache](repository-cache.md) | Publish tags and advertise `@v1.2.0`. A tag or branch is fetched by name, so depth does not come into it |
| Your build derives a version from Git history or tags | The cached repository has one commit and no tags | Keep history-derived versioning optional, or tolerate one commit with no tags |
| `global.json` pins an SDK version | Users whose installed SDKs do not satisfy the pin, as `dotnet` resolves it including its `rollForward` policy, get the SDK-not-found failure from `dotnet`; `dotnet-git-tool` retries once, outside your repository, only if they have a strictly newer SDK installed | Set `rollForward` in `global.json`, or do not pin an SDK version at all |
| Restore needs a private feed, a token, or an environment variable | `dotnet pack` runs on the user's machine with their environment and whatever NuGet configuration your repository commits | Keep the tool project restorable from public feeds with no secrets |
| The repository uses submodules | Submodules are never initialized, so a build that needs their content fails with kind `child_process_failed` | Do not put anything the tool project needs behind a submodule |
| The repository holds many `.csproj` files | Every candidate runs its own `dotnet msbuild` evaluation before anything is built | Commit `.config/dotnet-git-tool.json` with a `project` field, which cuts evaluation down to one project |
| The project builds only in `Debug` | Packing runs `dotnet pack --configuration Release` and fails | Keep `Release` building |

When a build does fail, your users see one line: the last line of the failing `git` or `dotnet` output. There is
no flag that shows the full build log. The recovery path is in [Troubleshooting](troubleshooting.md), and it
starts with `dotnet git-tool cache show JKamsker/bookmeta-cli` to get the cache directory and running
`dotnet pack` there by hand.

## What your repository does not control

- The generated package ID and version come from your source ID and the resolved commit. Nothing in your
  project changes them. See [How it works](how-it-works.md).
- Your project's version properties are not the installed package version. `PackageVersion`, then `Version`,
  then `VersionPrefix` are read from the `.csproj`, and from any `Directory.Build.props` between it and the
  repository root. The value appears in the version cell of `cache list` and as `sourceVersion` in
  `cache show --json`, described in [Repository cache](repository-cache.md). Human `cache show` output does not
  show it.
- The command style is your user's choice, not yours. You control the base name only.
- The generated package is written to a temporary directory on your user's machine, installed from there, and
  `dotnet-git-tool` deletes the directory afterward. It is not pushed to a NuGet feed.
- The cached repository is reset and cleaned around every build, so a build that writes into the working tree
  leaves nothing behind for the next install.

## What to put in your own README

Put the pinned form in your own README, together with one sentence about what installing does. The block below
is a template rather than a recipe to publish unchanged, because only the first line is literal.

On the second line, substitute your own `owner/repo` and a tag your repository actually publishes. On the third
line, use the invocation from the `Command:` clause of your install success line, which the verification run
above printed for your repository.

```console
dotnet tool install --global JKToolKit.Git.Tool
dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0
dotnet bookmeta
```

Leave `--yes` out of the line you publish. It answers the confirmation that warns your users the install builds
your code on their machine; they can add it themselves once they have decided, or in CI.

Say three things next to the commands: that installing builds your repository from source on the reader's own
machine, that `dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0 --dry-run` previews it first, and that they
need the .NET SDK and `git` on `PATH`. Mention `--standalone` if a plain `bookmeta` command suits your tool
better than `dotnet bookmeta`.

Advertise a tag rather than a branch. A branch moves on every push, so users who install a week apart get
different builds. `dotnet-git-tool` re-resolves the ref by name on every install and every `update`, so a tag
holds only as long as you do not move it.

## Author checklist

- [ ] Exactly one project in the repository is `PackAsTool=true`, or `.config/dotnet-git-tool.json` names the
      project.
- [ ] `ToolCommandName` (or the manifest's `command`) is set, and its base name matches
      `^[A-Za-z0-9][A-Za-z0-9_.-]*$`.
- [ ] The base name is distinctive enough that neither `dotnet-bookmeta` nor `bookmeta` collides with another
      global tool.
- [ ] `dotnet pack --configuration Release` succeeds with one commit, no tags, and no initialized submodules.
- [ ] `global.json`, if present, does not pin an SDK version your users are unlikely to have.
- [ ] Restore works from public feeds with no credentials and no environment variables.
- [ ] You have run install, `dotnet bookmeta`, `list`, and `uninstall` end to end on a clean machine.
- [ ] You have tagged a release and put the pinned install command in your README.

## See also

- [Documentation index](README.md)
- [How it works](how-it-works.md)
- [CLI reference](cli-reference.md)
- [Repository cache](repository-cache.md)
- [Troubleshooting](troubleshooting.md)
