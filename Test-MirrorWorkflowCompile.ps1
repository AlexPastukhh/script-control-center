#requires -Version 5.1
<#
Source-only compile check for the Docker-first WinForms mirror workflow UI.
Does not launch the app, inspect Git repositories, run Docker, pull, push,
switch branches or restart services. Run from the Windows Git clone.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'WINDOWS_ONLY' }
$source = Join-Path $PSScriptRoot 'Mirror-Workflow.UI.cs'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "WORKFLOW_SOURCE_NOT_FOUND: $source"
}
Add-Type -Path $source -ReferencedAssemblies @(
    'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll',
    'System.Drawing.dll'
) -ErrorAction Stop | Out-Null
$type = [FortisMirror.WorkflowWindow]
if ($null -eq $type.GetMethod('Open')) {
    throw 'WORKFLOW_OPEN_ENTRY_NOT_FOUND'
}
Write-Host 'MIRROR_WORKFLOW_WINFORMS_COMPILE_PASS'
