#requires -Version 5.1
# Standalone Gitleaks GUI; launcher is a CMD entry so Script Catalog's PS1 Bypass is not used.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -cne 'Windows_NT') { throw 'GITLEAKS_WINDOWS_REQUIRED' }
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'GITLEAKS_STA_REQUIRED: use Start-GitleaksScanner.cmd'
}
$engine = Join-Path $PSScriptRoot 'GitleaksScanEngine.cs'
$gui = Join-Path $PSScriptRoot 'GitleaksScannerForm.cs'
foreach ($source in @($engine, $gui)) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw 'GITLEAKS_GUI_SOURCE_MISSING'
    }
}
Add-Type -Path @($engine, $gui) -ReferencedAssemblies @(
    'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll',
    'System.Drawing.dll', 'System.Web.Extensions.dll'
) -ErrorAction Stop | Out-Null
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
$form = [FortisRdgControl.GitleaksScannerForm]::new()
[System.Windows.Forms.Application]::Run($form)
