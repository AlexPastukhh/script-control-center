#requires -Version 5.1
<#
Interactive Windows Git mirror inspector. Launch this same file from Script Control
Center to open a standalone Windows Forms UI. Enumerates mirror folders immediately;
checks a selected repository or all repositories only when you click a button.
The companion .cs UI and .ps1 CLI files must stay beside this launcher.

No automatic fetch/pull/commit/push or background check. Explicit owner-approved
Pull and Windows-only commit-all are available in the GUI.
Use -Cli for the original text-only report; -Notify applies only with -Cli.
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
if ($Cli) {
    & (Join-Path $PSScriptRoot 'Mirror-Status.Report.ps1') -MirrorRoot $MirrorRoot -Notify:$Notify
    return
}
if ($Notify) { Write-Warning '-Notify is available only with -Cli; opening the interactive window.' }
$git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
$source = Join-Path $PSScriptRoot 'Mirror-Status.UI.cs'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing UI source: $source" }
Add-Type -Path $source -ReferencedAssemblies @(
    'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll'
) -ErrorAction Stop | Out-Null
Write-Host 'Windows Git mirror UI is opening. Close its window to finish this script.'
[FortisMirror.MirrorWindow]::Open($MirrorRoot, $git)
Write-Host 'Windows Git mirror UI closed.'
