# Automation

This page covers running `dotnet-git-tool` (NuGet package `JKToolKit.Git.Tool`, invoked as
`dotnet git-tool`) from a script, a provisioning job, or a CI pipeline. An unattended run needs two decisions:
how you grant confirmation, and how you read the result. `dotnet-git-tool` starts only two external commands,
`git` and `dotnet`, so a container image carrying the .NET SDK and Git can run every `dotnet git-tool` command
below. The parsing examples additionally use `jq`, which a .NET SDK image does not carry; `ConvertFrom-Json`
or any other JSON parser reads the same envelope.

## The non-interactive contract

`install`, `update`, `uninstall`, and `cache prune` require confirmation. `--yes` grants it up front and
`--dry-run` sidesteps it by producing a preview instead of a mutation. Without either flag, the tool refuses
to prompt in four situations and fails with the error kind `confirmation_required` and exit code `2`; see
[Security](security.md#confirmation) for the list.

A CI step redirects standard input and standard error, and both are on that list, which is why a pipeline
that omits `--yes` fails with exit `2` even though nobody was there to answer a question. The rule for
automation is to pass `--yes` when you intend the change and `--dry-run` when you do not, and never to rely
on the interactive path.

```console
dotnet git-tool install JKamsker/bookmeta-cli
```

With both streams redirected and no `--yes`, that writes one line to stderr and exits `2`:

```text
error: Building 'JKamsker/bookmeta-cli' can execute arbitrary repository code. Inspect with --dry-run or explicitly consent with --yes.
```

Two details change how you write the flags. `--dry-run` wins over `--yes`: passing both produces a preview and
changes nothing, because the dry-run branch returns before confirmation is evaluated. And `cache prune` skips
the confirmation entirely when the repository cache holds nothing to remove, so a scheduled
`dotnet git-tool cache prune --json` succeeds there with exit `0` and no `--yes`, and returns
`confirmation_required` only when it would actually delete something.

> [!WARNING]
> Unknown options are ignored rather than rejected. `--dryrun --yes` is a real installation, because the
> misspelled flag is discarded and `--yes` satisfies the confirmation. Confirm a preview by checking the
> envelope for `"action": "install"`, not by assuming a typo would fail.

## Streams and verbosity

Every result goes to stdout in both modes, the JSON envelope included. Progress lines, `--verbose`
diagnostics, the confirmation prompt, and plain `error:` lines go to stderr. The
[CLI reference](cli-reference.md#streams) carries the full matrix.

`--quiet` suppresses progress lines, `--verbose` diagnostics, and the human success line of a real mutation,
and it makes the run non-interactive. It does not suppress the output of `list`, `cache list`, or `cache show`,
and it does not suppress the human message of a `--dry-run` mutation. Pass `--json` when you want one
parseable object and nothing else.

## The JSON envelope

`--json` replaces all human output with a single JSON object on stdout. A success envelope carries a `data`
object and a null `error`; a failure envelope carries a null `data` and an `error` object with `kind` and
`message`. Both always carry `meta.schemaVersion` and `meta.warnings`, and once a command starts executing
`--json` writes nothing at all to stderr, so any stderr content in JSON mode is a genuine crash.

Paths in the examples below use the Linux defaults; substitute your own cache root on macOS or Windows.

```console
dotnet git-tool install JKamsker/bookmeta-cli --dry-run --json
```

A success envelope:

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

The same command without `--dry-run` and without `--yes`:

```console
dotnet git-tool install JKamsker/bookmeta-cli --json
```

A failure envelope:

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

Those `\u0027` sequences are apostrophes: `System.Text.Json` web defaults escape `'` (and `<`, `>`,
`&`, `+`, and every non-ASCII character) in every string it writes, so a search for
`'JKamsker/bookmeta-cli'` in the raw bytes finds nothing, and a cache path under a non-ASCII user name is
escaped the same way. Decode with `jq`, `ConvertFrom-Json`, or any real parser before matching on message
text, and prefer matching on `error.kind`, which contains no escapable characters.

> [!IMPORTANT]
> `--json` guarantees an envelope only once a command body starts. An argument-parsing or validation error is
> handled earlier: it usually prints a plain `error: <MESSAGE>` line to stderr with stdout empty and exit `1`,
> but a branch invoked without a subcommand (`dotnet git-tool cache`) instead prints its help screen to stdout
> and exits `1` with nothing on stderr. Treat any non-zero exit without a parseable envelope on stdout as a
> hard failure.

## What each command puts in `data`

Mutating commands put an `action` discriminator in the `data` object and query commands do not, so a script
branches on `data.action` instead of comparing commits itself. The values are `install`, `installed`,
`update`, `updated`, `unchanged`, `uninstall`, `uninstalled`, `cache_prune_preview`, and `cache_pruned`. The
full key tables, for the `data` object and for the installation record and cached repository objects nested
inside it, are in the [CLI reference](cli-reference.md#the-json-envelope).

Three details of those payloads matter more in a script than in a reference table.

- `updatedAt` tracks real package changes, not update attempts. An `unchanged` result rewrites the
  installation record to reconcile `commandStyle` and `repositoryPath` but leaves `updatedAt` alone.
- `sizeBytes` is always null in `cache list` and populated only by `cache show`, because the size is computed
  on demand by walking the directory. A script that needs sizes calls `cache show` per cached repository.
- `unusedRepositoryPaths`, `removedRepositoryPaths`, and `skippedInUseRepositoryPaths` are arrays of absolute
  cache directory paths, which is what a cleanup step logs.

A preview reports from local state only. It performs no network access and never clones, which is why it works
offline and against a repository that does not exist. It is also not a guaranteed exit `0`: the state checks
run first, so `install --dry-run` on an already-managed source ID fails with `already_installed` (exit `6`),
and `update --dry-run` or `uninstall --dry-run` on an unmanaged source ID fails with `installation_not_found`
(exit `5`).

## Branching on exit codes

Only `0`, `1`, `2`, `5`, `6`, and `10` are ever returned. The set is sparse: `3`, `4`, `7`, `8`, and `9` never
appear, so do not treat the range as dense. One exit code covers several error kinds, so branch on
`error.kind` whenever the distinction matters. The canonical exit-code table and the full error-kind
vocabulary are in the [CLI reference](cli-reference.md#exit-codes).

Four decisions cover most failure handling:

- Retry later after `repository_cache_locked` or `state_locked` (exit `6`). Another run holds a lock, and
  waiting is the fix.
- Switch commands after `already_installed` (exit `6`) or `installation_not_found` (exit `5`). The first means
  run `update` instead of `install`, the second means the reverse.
- Grant confirmation after `confirmation_required` (exit `2`). The run needs `--yes`, not a retry.
- Stop on everything else. `child_process_failed` and `invalid_state` (exit `1`), `dependency_not_found`
  (exit `5`), and the remaining exit `2` kinds are deterministic rejections of your arguments, of the target
  repository's layout, or of a cache path, and running the command again reproduces them.

Exit `10` is the error kind `cancelled`, and it comes only from a confirmation prompt answered with anything
other than `y` or `yes`. An unattended run is refused before it can prompt, so it fails with
`confirmation_required` and exit `2` instead. Nothing in `dotnet-git-tool` handles Ctrl-C, so an interrupt
does not produce exit `10` and a script must not branch on `10` to detect one.

A failed `git` or `dotnet` command is reported as a single captured line, and no flag streams the full log, so
reproduce the build inside the cached repository to see the real error.
[Troubleshooting](troubleshooting.md) covers that path.

## Install or update in one step

Provisioning scripts usually want "make this tool present at this ref" rather than a strict install or update.
Try `install`, and fall back to `update` when the source ID is already managed. Both snippets read
`data.action`, which distinguishes `installed`, `updated`, and `unchanged` without comparing commits yourself.
`install` and `update` parse the repository argument identically, so either accepts the pin as
`owner/repo@<REF>` or as `--ref <REF>`, and `--ref` wins when both are given.

Linux and macOS:

```bash
#!/usr/bin/env bash
set -uo pipefail
repository="JKamsker/bookmeta-cli"
ref="v1.2.0"

envelope=$(dotnet git-tool install "$repository@$ref" --yes --json)
status=$?

# Exit 6 also covers cache and state locks, so confirm the kind before retrying.
if [ "$status" -eq 6 ] && [ "$(printf '%s' "$envelope" | jq -r '.error.kind')" = "already_installed" ]; then
  envelope=$(dotnet git-tool update "$repository@$ref" --yes --json)
  status=$?
fi

if [ "$status" -ne 0 ]; then
  printf '%s\n' "$envelope" >&2
  exit "$status"
fi
printf '%s' "$envelope" | jq -r '"\(.data.action) \(.data.installation.commit)"'
```

Windows (PowerShell):

```powershell
$repository = 'JKamsker/bookmeta-cli'
$ref = 'v1.2.0'

$output = dotnet git-tool install "$repository@$ref" --yes --json
$status = $LASTEXITCODE
# The envelope is indented, so PowerShell captures it as a string array.
# Join it before parsing: Windows PowerShell 5.1 will not parse the array.
$json = $output -join [Environment]::NewLine

# Exit 6 also covers cache and state locks, so confirm the kind before retrying.
if ($status -eq 6 -and ($json | ConvertFrom-Json).error.kind -eq 'already_installed') {
    $output = dotnet git-tool update "$repository@$ref" --yes --json
    $status = $LASTEXITCODE
    $json = $output -join [Environment]::NewLine
}

if ($status -ne 0) {
    Write-Error $json
    exit $status
}
$envelope = $json | ConvertFrom-Json
"$($envelope.data.action) $($envelope.data.installation.commit)"
```

## Reading the envelope with jq

List every managed source ID, one per line:

```bash
dotnet git-tool list --json | jq -r '.data.installations[].sourceId'
```

With one source tool installed, that prints:

```text
JKamsker/bookmeta-cli
```

Print the commit installed for one source ID, which is the value to keep in an audit log:

```bash
dotnet git-tool list --json | jq -r '.data.installations[] | select(.sourceId == "JKamsker/bookmeta-cli") | .commit'
```

List the cache directories a prune would delete, without deleting anything:

```bash
dotnet git-tool cache prune --dry-run --json | jq -r '.data.unusedRepositoryPaths[]'
```

## Isolating the repository cache and installation state in CI

`DOTNET_GIT_TOOL_CACHE` and `DOTNET_GIT_TOOL_HOME` redirect the repository cache and the installation state
file. Both name a directory. Set both to absolute paths: a relative value is accepted and resolved against
the working directory of whichever step runs the command, so one variable can point at two different places
in a single job. Pointing them inside the workspace gives a job a known starting state and makes the cache
eligible for the pipeline's own caching step. [Configuration](configuration.md) carries the full precedence
chains and the defaults they replace.

Only `install` and `update` reach the Git host. `uninstall`, `list`, `cache list`, `cache show`,
`cache prune`, and every `--dry-run` work against local state, though a real `install` or `update` also
restores the generated package from whatever NuGet feeds the machine has configured.

Authentication is whatever `git` already has. A credential helper that tries to prompt makes the step hang
with an empty log, because child output is buffered and stdin is inherited, so give the runner a
non-interactive credential (see [Security](security.md#credentials)).

Concurrent runs on one machine take a repository lock and a state lock rather than corrupting each other, and
a run that waits too long fails with `repository_cache_locked` or `state_locked` (exit `6`). `cache prune` is
the exception: it never waits, and reports a locked cached repository in `skippedInUseRepositoryPaths` while
still exiting `0`. The [repository cache](repository-cache.md) page carries the timeouts.

## A GitHub Actions job

This job installs a source tool pinned to a tag and then runs it. Two values in it come from the target
repository rather than from `dotnet-git-tool`, so adapt them before running it: `v1.2.0` stands in for a tag
that repository actually publishes, and the invocation is read back from the installation record rather than
guessed from the repository name.

```yaml
name: Install a source tool

on:
  workflow_dispatch:

jobs:
  install:
    runs-on: ubuntu-latest
    env:
      # Absolute paths inside the workspace, so the job never touches the
      # runner's shared cache or state and starts from a known state.
      DOTNET_GIT_TOOL_CACHE: ${{ github.workspace }}/.dotnet-git-tool/cache
      DOTNET_GIT_TOOL_HOME: ${{ github.workspace }}/.dotnet-git-tool/state
    steps:
      - name: Install the .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      # dotnet tool install --global writes into this directory on Linux, and
      # the dotnet driver finds a dotnet subcommand by searching PATH.
      - name: Put .NET global tools on PATH
        run: echo "$HOME/.dotnet/tools" >> "$GITHUB_PATH"

      - name: Install dotnet-git-tool
        run: dotnet tool install --global JKToolKit.Git.Tool

      # --dry-run reports the plan from local state: it does not clone, so it
      # catches a malformed repository argument or an already-managed source
      # ID, but it does not check that the repository or the ref exists.
      - name: Preview the install
        run: dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0 --dry-run --json

      # The @ref pins the build to a tag, so the job is reproducible. --yes
      # grants confirmation: stderr is redirected here, so without it the step
      # fails with confirmation_required and exit 2. --json puts the envelope
      # on stdout and leaves stderr empty.
      - name: Install the source tool
        run: dotnet git-tool install JKamsker/bookmeta-cli@v1.2.0 --yes --json

      - name: Record the installed commit
        run: dotnet git-tool list --json | jq -r '.data.installations[].commit'

      # The invocation is recorded, not derived from the repository name, so
      # read it back. The default command style records a dotnet subcommand;
      # --standalone records an unprefixed command.
      - name: Run the source tool
        run: |
          invocation=$(dotnet git-tool list --json | jq -r '.data.installations[].command')
          $invocation
```

The preview step earns its place by catching argument mistakes and recording the intended plan in the job
log, not by verifying the repository. Repeat `--ref v1.2.0` on later updates to retain that pin. Omit a ref
on `update` to remove the pin and use the remote default branch.

## What `meta.schemaVersion` 1 covers

This is the envelope's schema version. Every envelope this version emits carries `meta.schemaVersion: 1`.
Assert that value before parsing, and fail the step if it differs rather than guessing at an envelope you do
not recognize.

`installed.json` carries its own `schemaVersion`, the state schema version, and a state file the tool does not
recognize fails every command with `invalid_state` and exit `1`, `list`, `cache list`, and `cache prune`
included. That matters when a pipeline restores a cached state directory, and
[Configuration](configuration.md) owns that file.

Covered by envelope schema version 1: the four top-level keys `ok`, `data`, `error`, and `meta`; the shape of
the `error` object; the `data` keys and `action` values; the installation record and cached repository fields;
and the rule that the envelope goes to stdout. `meta.warnings` is an array, and it is empty in every envelope
the current version produces.

Not covered, and not safe to parse: human output of any kind, including the `list` column widths and
truncation, the `cache list` table borders and dates, the `cache show` label layout, the preview and success
sentences, and the progress lines on stderr. The generated version string (`0.0.0-git.4fbe47e66359.dotnet`) is
derived from the commit and the command style; read `commit` and `commandStyle` from the record instead of
taking the version apart.

## See also

- [Documentation index](README.md)
- [CLI reference](cli-reference.md): every command, option, exit code, and error kind.
- [Configuration](configuration.md): the environment variables used here and the paths they replace.
- [Security](security.md): why installing and updating require confirmation at all.
- [Troubleshooting](troubleshooting.md): symptom to fix for each error kind.
