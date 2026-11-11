#requires -Version 5.1
<#
Stable Script Center Mirror entry. Normal GUI opens a one-use loopback Browser
session. This does not edit owner catalog, Windows Git, vault or network.
Set registered Script Catalog entry to "window" to see startup/errors.
Explicit -Cli / -Notify keep the legacy reporter.
#>
[CmdletBinding()]
param(
    [string]$MirrorRoot = (Join-Path $env:USERPROFILE 'Repos'),
    [switch]$Cli,
    [switch]$Notify
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'MIRROR_WINDOWS_REQUIRED' }

if ($Cli -or $Notify) {
    # Explicit legacy reporting options retain existing non-browser semantics.
    $sourceRepo = Join-Path (Join-Path $env:USERPROFILE 'Repos') 'docke-windows-mirror'
    $legacy = Join-Path $sourceRepo 'Fortis-Windows-Mirror-Status.ps1'
    if (-not (Test-Path -LiteralPath $sourceRepo -PathType Container)) {
        throw 'MIRROR_SOURCE_CLONE_MISSING'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRepo '.git'))) {
        throw 'MIRROR_SOURCE_NOT_GIT_CLONE'
    }
    if (-not (Test-Path -LiteralPath $legacy -PathType Leaf)) {
        throw 'MIRROR_LEGACY_LAUNCHER_MISSING'
    }
    $git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
    $origin = @(& $git -C $sourceRepo remote get-url origin 2>$null)
    if ($LASTEXITCODE -ne 0 -or $origin.Count -ne 1 -or
        $origin[0] -notmatch '^(?i:https://github\.com/AlexPastukhh/docke-windows-mirror(?:\.git)?|git@github\.com:AlexPastukhh/docke-windows-mirror(?:\.git)?)$') {
        throw 'MIRROR_SOURCE_UNEXPECTED_ORIGIN'
    }
    & $legacy -MirrorRoot $MirrorRoot -Cli:$Cli -Notify:$Notify
    return
}

# Do not migrate the owner catalog: old records use this filename.
$browser = Join-Path $PSScriptRoot 'Fortis-Mirror-Browser.ps1'
if (-not (Test-Path -LiteralPath $browser -PathType Leaf)) {
    throw 'MIRROR_BROWSER_ADAPTER_MISSING_UPDATE_SCRIPT_CENTER'
}
& $browser -MirrorRoot $MirrorRoot
