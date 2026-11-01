$ErrorActionPreference = 'Stop'
try {
    $source = Join-Path $PSScriptRoot 'ControlCenter.cs'
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Не найден $source" }
    Add-Type -Path @($source, (Join-Path $PSScriptRoot 'ScriptCatalog.cs')) -ReferencedAssemblies @(
      'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll',
      'System.Drawing.dll', 'System.Management.dll', 'System.Xml.dll'
    ) -ErrorAction Stop | Out-Null
    if (-not ('FortisRdgControl.MainForm' -as [type])) { throw 'MainForm type не обнаружен после компиляции.' }
    Write-Host 'COMPILATION_OK — WinForms C# собирается в Windows PowerShell.' -ForegroundColor Green
} catch {
    Write-Host ('COMPILATION_FAILED: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host ($_ | Out-String)
    exit 1
}
