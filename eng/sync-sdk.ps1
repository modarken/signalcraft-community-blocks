<#
.SYNOPSIS
    Put the SignalCraft package this library compiles against into the local feed.

.DESCRIPTION
    A block here derives from BlockBase and implements IHostedBlock, so the
    NuGet half of this repository needs SignalCraft.Hosting to compile.
    SignalCraft is not published anywhere, so the package has to be produced
    locally and dropped into artifacts/nuget, which NuGet.config lists as a
    source. This script is the documented way to do it.

    THE VERSION COMES FROM A GIT TAG. SignalCraft's build derives its version
    from `git describe`, so a build made AT the tag v0.3.0 is version 0.3.0,
    and a build made one commit later is 0.3.1-dev.1.g<sha> - a prerelease of
    the NEXT patch, and a different package as far as NuGet is concerned. So
    "the 0.3.0 package" means, precisely, one packed from the tagged commit.

    This library pins an exact released version on purpose. It is published to
    strangers, and a library that floats with whatever SDK happened to be on
    the author's machine is not something anybody can depend on.

.PARAMETER SignalCraftRepo
    A signalcraft checkout, or any folder with a nuget feed under it. Defaults
    to a sibling of this repository.

.PARAMETER Version
    The SDK version to install. Must match the pin in
    csharp/SignalCraft.Community.Blocks.csproj.

.EXAMPLE
    ./eng/sync-sdk.ps1
    ./eng/sync-sdk.ps1 -Version 0.4.0
#>
[CmdletBinding()]
param(
    [string]$SignalCraftRepo = '',
    [string]$Version = '0.3.0'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$feed     = Join-Path $repoRoot 'artifacts\nuget'

if (-not $SignalCraftRepo) {
    $SignalCraftRepo = Join-Path (Split-Path $repoRoot -Parent) 'signalcraft'
}

# SignalCraft.Hosting AND WHAT IT DEPENDS ON. packageSourceMapping sends every
# SignalCraft.* request to the local feed and nowhere else, so a transitive
# dependency missing from the feed is a hard restore failure rather than a
# silent fall-through to nuget.org - which is the whole point of the mapping.
#
# Named in full rather than globbed: a feed that quietly contained the wrong
# thing would restore perfectly well right up to the first `using`.
$packages = @(
    "SignalCraft.Hosting.$Version.nupkg",
    "SignalCraft.Abstractions.$Version.nupkg",
    "SignalCraft.Core.$Version.nupkg"
)

if (-not (Test-Path $feed)) { New-Item -ItemType Directory -Path $feed | Out-Null }

$missing = $packages | Where-Object { -not (Test-Path (Join-Path $feed $_)) }
if ($missing.Count -eq 0) {
    Write-Host "already present: $($packages -join ', ')" -ForegroundColor Green
    exit 0
}

if (-not (Test-Path $SignalCraftRepo)) {
    throw "no signalcraft checkout at $SignalCraftRepo - pass -SignalCraftRepo"
}

# Where package-sdk.ps1 writes, and where a consumer that has already synced
# keeps its copy. Either will do; the file is the same file.
$candidates = @(
    (Join-Path $SignalCraftRepo 'artifacts\sdk\nuget'),
    (Join-Path $SignalCraftRepo 'artifacts\nuget')
)

foreach ($package in $missing) {
    $found = $null
    foreach ($dir in $candidates) {
        $candidate = Join-Path $dir $package
        if (Test-Path $candidate) { $found = $candidate; break }
    }

    if (-not $found) {
        throw @"
$package not found under $SignalCraftRepo.

It is packed from the TAGGED commit, not from whatever is checked out:

    cd $SignalCraftRepo
    git worktree add ../signalcraft-v$Version v$Version
    cd ../signalcraft-v$Version
    ./scripts/package-sdk.ps1 -Configuration Release

then run this script again.
"@
    }

    Copy-Item $found (Join-Path $feed $package)
    Write-Host "copied $package" -ForegroundColor Green
    Write-Host "  from $found"
}
