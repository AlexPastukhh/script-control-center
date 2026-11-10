#requires -Version 5.1
# Source-only compile check; never launches a scan, Docker or services.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -cne 'Windows_NT') { throw 'GITLEAKS_COMPILE_WINDOWS_REQUIRED' }
$root = Split-Path -Parent $PSScriptRoot
$files = @(
    (Join-Path $root 'GitleaksScanEngine.cs'),
    (Join-Path $root 'GitleaksScannerForm.cs')
)
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Missing source: ' + $file) }
}
Add-Type -Path $files -ReferencedAssemblies @(
    'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll',
    'System.Drawing.dll', 'System.Web.Extensions.dll'
) -ErrorAction Stop | Out-Null
if (-not ('FortisRdgControl.GitleaksScannerForm' -as [type])) {
    throw 'Gitleaks UI type not found'
}
Write-Host 'GITLEAKS_SOURCE_COMPILE_PASS'
