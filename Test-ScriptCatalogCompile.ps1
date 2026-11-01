#requires -Version 5.1
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Only Windows is supported.' }
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run with Windows PowerShell -STA.'
}
$catalog = Join-Path $PSScriptRoot 'ScriptCatalog.cs'
if (-not (Test-Path -LiteralPath $catalog -PathType Leaf)) {
    throw "Не найден исходник: $catalog"
}
try {
    Add-Type -Path $catalog -ReferencedAssemblies @(
        'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll',
        'System.Drawing.dll', 'System.Management.dll', 'System.Xml.dll'
    ) -ErrorAction Stop | Out-Null
    if (-not ('FortisRdgControl.ScriptCatalogForm' -as [type])) {
        throw 'ScriptCatalogForm type не обнаружен после компиляции.'
    }
    Write-Host 'SCRIPT_CATALOG_COMPILE_OK — агрегатор собран; GUI не запускался.' -ForegroundColor Green
} catch {
    Write-Host ('SCRIPT_CATALOG_COMPILE_FAILED: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host ($_ | Out-String)
    exit 1
}
