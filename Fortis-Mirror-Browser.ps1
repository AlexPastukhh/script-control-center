#requires -Version 5.1
<#
Script Center adapter for the accepted Fortis Mirror browser UI.
The UI/API sources live ONLY in the sibling Windows docke-windows-mirror
Git clone. Launch is read-only; this adapter never runs git fetch/pull,
updates catalog.xml, installs a service, or configures Gateway.
Register in Script Catalog with mode "window" (not "hidden").
#>
[CmdletBinding()]
param(
    [string]$MirrorRoot = (Join-Path $env:USERPROFILE 'Repos'),
    [ValidateRange(1024, 65535)][int]$Port = 8765
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'MIRROR_WINDOWS_REQUIRED' }

$sourceRepo = Join-Path (Join-Path $env:USERPROFILE 'Repos') 'docke-windows-mirror'
$launcher = Join-Path $sourceRepo 'Start-MirrorAPI.ps1'
if (-not (Test-Path -LiteralPath $sourceRepo -PathType Container)) {
    throw ('MIRROR_SOURCE_CLONE_MISSING: ' + $sourceRepo)
}
if (-not (Test-Path -LiteralPath (Join-Path $sourceRepo '.git'))) {
    throw ('MIRROR_SOURCE_NOT_GIT_CLONE: ' + $sourceRepo)
}
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw ('MIRROR_BROWSER_LAUNCHER_MISSING: ' + $launcher +
        '. Pull the approved docke-windows-mirror/main to Windows first.')
}
$git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
$origin = @(& $git -C $sourceRepo remote get-url origin 2>$null)
if ($LASTEXITCODE -ne 0 -or $origin.Count -ne 1 -or
    $origin[0] -notmatch '^(?i:https://github\.com/AlexPastukhh/docke-windows-mirror(?:\.git)?|git@github\.com:AlexPastukhh/docke-windows-mirror(?:\.git)?)$') {
    throw 'MIRROR_SOURCE_UNEXPECTED_ORIGIN'
}
$branch = @(& $git -C $sourceRepo symbolic-ref --quiet --short HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or $branch.Count -ne 1 -or $branch[0] -cne 'main') {
    throw 'MIRROR_SOURCE_NOT_ON_MAIN'
}
# The browser observes dirty repositories; do not reset, stash or clean them.
# Presence of the accepted source is checked locally without network Git.
$null = (Get-Command py.exe -CommandType Application -ErrorAction Stop)
$null = (Get-Command docker.exe -CommandType Application -ErrorAction Stop)
Write-Host 'Starting Fortis Mirror Browser (loopback only, read-only)...'
& $launcher -MirrorRoot $MirrorRoot -Port $Port -OpenBrowser
