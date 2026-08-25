# Security

`dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as `dotnet git-tool`) builds a repository on your
machine and installs the result as a .NET global tool. That is the whole feature, and it is the center of the
threat model. This page states plainly what executes, when you are asked to consent, what `dotnet-git-tool`
protects, and what it does not do.

Paths in the examples use the Linux defaults. Commits, versions, and dates are illustrative and differ on your
machine.

## Installing from source runs that repository's build

Installing or updating a [source tool](README.md#glossary) runs the target repository's build under your user
account, with your environment, your `PATH`, and your NuGet configuration. Every stage of a .NET build can
execute code the repository controls:

- Project evaluation loads the repository's imports (`Directory.Build.props`, `.targets` files, custom SDKs) and
  evaluates the property functions in them, so it is not a passive read of the `.csproj`.
- Restore downloads whatever packages the project references. The build runs inside the cached repository, so the
  NuGet configuration that applies is the one for that directory, which includes any `nuget.config` the
  repository commits, not only the feeds you configured yourself.
- Build and pack execute MSBuild targets and tasks, `Exec` steps that run shell commands or scripts, source
  generators, and analyzers, including the ones the restored packages bring in.

Installing a repository is therefore equivalent to trusting its owner and everyone who can push to it.
`dotnet-git-tool` says so itself: the `install --dry-run` and `update --dry-run` payloads carry
`executesRepositoryCode: true`.

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

The envelope shape and the full `data` keys for each command are in the [CLI reference](cli-reference.md).

The trust does not end when the install finishes. The command `dotnet tool install --global` puts in your global
tools directory is that repository's program, and it runs with your privileges every time you invoke it.

## What runs, and when

| Command | Clones or fetches | Runs repository code | Changes installed tools | Deletes cached repositories |
|---|---|---|---|---|
| `install` | yes | yes | yes | no |
| `install --dry-run` | no | no | no | no |
| `update` | yes | only if the commit or command style changed | only if the commit or command style changed | no |
| `update --dry-run` | no | no | no | no |
| `uninstall` | no | no | yes | no |
| `uninstall --dry-run` | no | no | no | no |
| `list` | no | no | no | no |
| `cache list` | no | no | no | no |
| `cache show` | no | no | no | no |
| `cache prune` | no | no | no | yes |
| `cache prune --dry-run` | no | no | no | no |

`update` always fetches first, then compares the resolved commit and the command style against the installation
record. When both match it rewrites the record and stops, so no project evaluation, build, or pack happens.

`cache list` and `cache show` run read-only `git` queries against directories that are already in the repository
cache (`remote get-url`, `rev-parse`, `branch`, `describe`, `show`, `status`). They never evaluate, build, or
pack anything. `cache prune` runs no `git` command at all; it deletes unused cached repositories and never
touches an installed tool.

`--dry-run` returns before the confirmation prompt and before the clone. For `install` and `update` it computes
the cache directory from the [source ID](README.md#glossary), checks whether a `.git` directory is already there,
prints the plan, and exits. Every `--dry-run` performs no network access and starts no external command.

A successful preview is not evidence that the repository is safe, or that it exists. `--dry-run` does not verify
the repository, the requested ref, or a `--project` path. It reads local state only, which is why
`install --dry-run` still fails with `already_installed` (exit `6`) for a repository you already manage, and
`update --dry-run` and `uninstall --dry-run` still fail with `installation_not_found` (exit `5`) for one you do
not. Kinds like these are the `error.kind` value in `--json` output; the full list is in the
[CLI reference](cli-reference.md#error-kinds).

## Confirmation

Every real mutation asks for confirmation first: `install`, `update`, `uninstall`, and a `cache prune` that would
remove at least one cached repository. `--dry-run` skips the prompt because it returns earlier. `-y` or `--yes`
means "I have decided, do not ask", and is the only way to proceed without a prompt.

Interactively, `install` and `update` print this to stderr and read one line from stdin:

```text
Warning: building 'JKamsker/bookmeta-cli' can execute arbitrary code from that repository.
Continue? [y/N]
```

The second line ends with a space rather than a newline, so your answer appears beside the prompt. Only `y` or
`yes` continues, compared without regard to case. Anything else, including an empty line, raises
`Operation cancelled.` with kind `cancelled` and exit code `10`. Nothing is cloned, built, or installed.

The prompt is refused rather than shown whenever it cannot be answered honestly. That happens with `--json`, with
`--quiet`, when stdin is redirected, and when stderr is redirected. In all four cases the command fails with kind
`confirmation_required` and exit code `2`, and changes nothing:

```console
dotnet git-tool install JKamsker/bookmeta-cli --json
```

Output (the `\u0027` sequences are real: `System.Text.Json` web defaults escape the apostrophe):

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

`uninstall` and `cache prune` refuse the same way with their own wording: `Uninstalling 'JKamsker/bookmeta-cli'
requires confirmation. Inspect with --dry-run or confirm with --yes.` and `Removing 3 unused cached repositories
requires confirmation. Inspect with --dry-run or explicitly confirm with --yes.` A `cache prune` that finds
nothing to remove neither prompts nor refuses; it reports `Removed 0 unused cached repositories.` and exits `0`.

> [!WARNING]
> Misspelled options are ignored, not rejected. `--dryrun --yes` performs a real installation, because the
> unknown flag is discarded and `--yes` answers the confirmation. Confirm a preview by looking for the
> `Would prepare` line shown below, or for `"action": "install"` in the envelope.

## Reducing the risk

### Before you install

1. Read the source. Open the repository on its host and read the project files, `Directory.Build.props`, any
   `.targets` file, and any `Exec` or pre-build step. `dotnet-git-tool` does not review the repository for you.
2. Preview with `--dry-run`. It reports the resolved source ID, the command style, and the cache directory
   without cloning.
3. Pin to a reviewed tag or commit instead of tracking a branch:
   `dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0`. Review the ref you pin, not the repository in general:
   a tag is a movable pointer, and moving it is something anyone who can push to the repository can do. A tag or
   branch is fetched by name; a raw commit hash works only when the remote allows fetching it directly, because
   clones are shallow. See [Repository cache](repository-cache.md) for what shallow clones imply.

This is the preview for the example repository:

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run
```

Output:

```text
Would prepare cached sources for JKamsker/bookmeta-cli, discover a tool project, pack it for a 'dotnet <command>' invocation, install it globally, and retain clean sources at /home/you/.cache/dotnet-git-tool/repositories/JKamsker-bookmeta-cli-1cd22d4b86ac.
```

### Ongoing

- Re-review before you update. `update` fetches the current commit of the recorded ref and rebuilds it, which
  runs that new commit's code. The pin itself persists: after installing `JKamsker/bookmeta-cli@v1.2.0`, plain
  `update JKamsker/bookmeta-cli` stays on `v1.2.0`, and uninstalling and installing again is how you move off a
  pin. `update` also takes the clone URL, the project path, and the command style from the installation record
  rather than from your argument, so passing a different URL that normalizes to the same source ID does not
  repoint the remote.
- Treat an untrusted repository as untrusted infrastructure. Build it on a throwaway machine or in a container,
  and point `DOTNET_GIT_TOOL_CACHE` and `DOTNET_GIT_TOOL_HOME` at directories inside it so nothing reaches your
  real repository cache or installation state file. Both are defined in [Configuration](configuration.md).
- Audit afterwards. `dotnet git-tool list` shows the generated package ID, the installed commit, and the
  invocation; `dotnet git-tool cache show JKamsker/bookmeta-cli` shows the full commit and the retained sources.
- Remove it if you change your mind. `dotnet git-tool uninstall JKamsker/bookmeta-cli --yes` removes the global
  tool and its installation record, and reports `Cached sources retained at <CACHE_DIRECTORY>`: the source code
  stays in the repository cache. Run `dotnet git-tool cache prune --yes` afterwards to delete it, and see
  [Repository cache](repository-cache.md) for what prune counts as unused.

## What `dotnet-git-tool` protects

These are integrity and hygiene properties. They limit accidents and protect your own data. None of them stops a
hostile build.

- A requested ref is validated before it reaches `git`. A value that starts with `-`, contains whitespace or a
  control character, or is longer than 1024 characters is rejected with kind `invalid_ref` and exit code `2`,
  which stops `JKamsker/bookmeta-cli@--upload-pack=evil` from being reinterpreted by `git` as an option. A test
  pins the behavior. Passed as `--ref --upload-pack=evil` the argument parser rejects it first, with exit code
  `1`.
- External commands are started directly, without a shell, with one argument per list entry. No value you supply
  (the repository argument, `--ref`, `--project`) is ever passed to a shell. The one shell-evaluated argument is
  a fixed internal string used by `git submodule foreach` during the clean.
- The project that gets built cannot escape the cached repository. Both `--project` and the `project` field of
  the repository manifest are resolved against the cache directory, and a path outside it is refused with kind
  `invalid_project` and exit code `2`.
- The repository cache is reset and cleaned every time an existing cached repository is reused, again after a
  successful pack, and again when the cache handle is disposed, which includes the failure path. A build cannot
  quietly leave files in your repository cache. This is hygiene rather than containment: the clean happens after
  the build, so it does not prevent a build from writing anywhere else on your machine. The mechanism is
  described in [Repository cache](repository-cache.md).
- The generated package is written outside the cache. `dotnet pack` runs with the cached repository as its
  working directory, so the build's `obj/` and `bin/` output lands inside the cached repository and the clean
  removes it afterwards. Only the `.nupkg` goes to a fresh temporary directory named
  `dotnet-git-tool-package-<GUID>`, which is deleted after the install.
- A directory sitting at the cache path that is not a Git repository is refused with kind
  `invalid_repository_cache` and left exactly as it was, and a cached repository whose origin does not match the
  requested clone URL is refused with kind `repository_cache_conflict`. Neither case deletes anything.
- `cache prune` deletes only direct children of `<CACHE_ROOT>/repositories`. Any other path raises kind
  `invalid_cache_prune_path`, and the command only ever considers direct children of that directory in the first
  place. A cached repository held by a concurrent operation is skipped rather than deleted.
- The installation state file is written atomically, to a temporary file that then replaces the old one.
- If the global install succeeds but writing the installation record fails, `dotnet-git-tool` attempts to remove
  it again with `dotnet tool uninstall --global <PACKAGE_ID>` and then reports the original failure.

## What `dotnet-git-tool` does not do

- It does not sandbox or isolate the build. The build runs as you, with your environment variables, your `PATH`,
  your NuGet configuration and feed credentials, your SSH agent, and unrestricted network access.
- It does not restrict the network during restore or build.
- It does not verify any signature or provenance: not the repository, not the commit, not the restored packages,
  and not the package it generates.
- It records no dependency hashes and verifies none. There is no lockfile and no checksum of the generated
  package, so nothing here guarantees that two installs of the same commit restore the same dependency versions.
- It does not verify that a tag still points at the commit you reviewed. If the repository owner moves the tag,
  the next `update` builds whatever the tag points at then.
- It keeps no allowlist or denylist of repositories, owners, or hosts.
- It never inspects repository content for anything harmful. It reads the repository manifest and project files,
  and it reads them only to decide what to build and what version to report.
- It does not verify the installation state file. `installed.json` is plain, unsigned JSON in your state
  directory, and `update` takes the clone URL recorded there rather than the one you type.
- It does not confirm that a URL is the repository you meant. An HTTP(S) or `ssh://` URL is reduced to its host
  plus the last two path segments, and the host is dropped for `github.com`. Two repositories on the same host
  that differ only above those segments collapse onto one identity, one cache directory, and one generated
  package ID. The full grammar is in the [CLI reference](cli-reference.md).
- It does not initialize submodules, so a repository that needs submodule content to build fails rather than
  fetching it.
- It does not show you the build output. A failed `git` or `dotnet` command is reported with kind
  `child_process_failed` and the last non-empty line of that command's output, and no flag streams the full log.
  See [Troubleshooting](troubleshooting.md) for how to reproduce a build by hand.

## Credentials

`dotnet-git-tool` performs all network access by starting `git` and `dotnet`, and those children inherit your
environment. It contains no credential handling code: no credential option, no prompt of its own, and no
credential storage. Private repositories work when your existing Git credential helper or SSH agent can answer
without interaction, exactly as they would for a `git clone` you typed yourself.

Child process output is captured rather than streamed, and the child inherits stdin. A credential helper that
tries to prompt therefore makes the command look like it has hung with no output. Cancel it, run `git clone`
against the same URL by hand to satisfy the helper, and retry.

## The generated package

`install` packs the discovered project into a NuGet package named after the source ID, for example
`git.JKamsker.bookmeta-cli` at version `0.0.0-git.4fbe47e66359.dotnet`. The derivation of both is described in
[How it works](how-it-works.md).

That package exists only on your machine. It is written into a `packages` subdirectory of the temporary pack
directory, installed from there with `--add-source` and `--ignore-failed-sources`, and the temporary directory is
deleted afterwards. It is never pushed to nuget.org or to any other feed. `--add-source` adds that directory
alongside the feeds your NuGet configuration already provides rather than replacing them.

The 12-character commit inside the version string is the identity of what you actually installed. Read the
package version and the full 40-character commit with `dotnet git-tool cache show JKamsker/bookmeta-cli`, or read
them from the `version` and `commit` fields of `dotnet git-tool list --json`. The human `list` table shows the
commit truncated to fit its column and does not show the version. These are the audit trail for a source tool,
since the generated version carries no other provenance.

## Reporting a vulnerability

Report a security problem in `dotnet-git-tool` itself through
[GitHub issues on JKamsker/dotnet-git-tool](https://github.com/JKamsker/dotnet-git-tool/issues), or through
GitHub's private vulnerability reporting on that repository if it is enabled. The repository publishes no
`SECURITY.md` and no private reporting instructions. Include the command you ran, the exit code, and the
`error.kind` from `--json` output where one applies.

A problem in a repository you installed from belongs to that repository's maintainer, not here.

## See also

- [Documentation index](README.md)
- [CLI reference](cli-reference.md)
- [Automation](automation.md)
- [Repository cache](repository-cache.md)
- [Configuration](configuration.md)
