#requires -Version 5.1
<#
Stable Script Center launch point for the standalone Docker-Windows Mirror.
All Mirror application sources and tests live in the sibling Git clone
%USERPROFILE%\Repos\docke-windows-mirror. This wrapper neither modifies
Git nor automatically clones/pulls source. No service is started or restarted.
#>
[CmdletBinding()]
param(
    [string]$MirrorRoot = (Join-Path $env:USERPROFILE 'Repos'),
    [switch]$Cli,
    [switch]$Notify
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows only.' }

$sourceRepo = Join-Path (Join-Path $env:USERPROFILE 'Repos') 'docke-windows-mirror'
$launcher = Join-Path $sourceRepo 'Fortis-Windows-Mirror-Status.ps1'
if (-not (Test-Path -LiteralPath $sourceRepo -PathType Container)) {
    throw "MIRROR_SOURCE_CLONE_MISSING: $sourceRepo. First run: git clone https://github.com/AlexPastukhh/docke-windows-mirror.git `"$sourceRepo`""
}
if (-not (Test-Path -LiteralPath (Join-Path $sourceRepo '.git'))) {
    throw "MIRROR_SOURCE_NOT_GIT_CLONE: $sourceRepo"
}
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw "MIRROR_SOURCE_LAUNCHER_MISSING: $launcher. Check your Windows clone."
}
$git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
$origin = @(& $git -C $sourceRepo remote get-url origin 2>$null)
if ($LASTEXITCODE -ne 0 -or $origin.Count -ne 1 -or
    $origin[0] -notmatch '^(?i:https://github\.com/AlexPastukhh/docke-windows-mirror(?:\.git)?|git@github\.com:AlexPastukhh/docke-windows-mirror(?:\.git)?)$') {
    throw "MIRROR_SOURCE_UNEXPECTED_ORIGIN: $sourceRepo"
}
& $launcher -MirrorRoot $MirrorRoot -Cli:$Cli -Notify:$Notify
