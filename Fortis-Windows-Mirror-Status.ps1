#requires -Version 5.1
<#
Windows Docker-first Git workflow inspector. The main UI enumerates Docker Git
repositories by read-only docker exec on opening. GitHub and Windows checks
run only after owner presses Check. No background monitoring, automatic pull,
push, commit or checkout. The Docker + Windows active branches are separate
from independently selected view branches. Explicit owner-confirmed fast-forward
Windows pull and guarded branch switch are supported.
The new UI source and Docker exporter must match this launcher version.
Use -Cli for the older main-only console report; -Notify is CLI-only.
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
$source = Join-Path $PSScriptRoot 'Mirror-Workflow.UI.cs'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing UI source: $source" }
Add-Type -Path $source -ReferencedAssemblies @(
    'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll'
) -ErrorAction Stop | Out-Null
Write-Host 'Windows Git mirror UI is opening. Close its window to finish this script.'
[FortisMirror.WorkflowWindow]::Open($MirrorRoot, $git)
Write-Host 'Windows Git mirror UI closed.'
