[CmdletBinding()]
param(
    [string] $BaseTag = "nuget-v0.0.0"
)

$ErrorActionPreference = "Stop"
$versionPrefix = "0.0"
$firstPublishedPatch = 1

git rev-parse --verify --quiet "refs/tags/$BaseTag" *> $null
if ($LASTEXITCODE -ne 0) {
    throw "The package-version baseline tag '$BaseTag' is missing. Fetch the complete Git history and tags."
}

$patchText = git rev-list --count "$BaseTag..HEAD"
$patch = 0
if ($LASTEXITCODE -ne 0 -or -not [int]::TryParse($patchText, [ref] $patch)) {
    throw "Could not calculate the package patch version from Git history."
}

if ($patch -lt $firstPublishedPatch) {
    throw "The package version must be at least $versionPrefix.$firstPublishedPatch. Commit after '$BaseTag' before packaging."
}

"$versionPrefix.$patch"
