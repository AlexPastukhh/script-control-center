#requires -Version 5.1
# Standalone launcher for the user-script aggregator only.
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Only Windows is supported.' }
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Start with Windows PowerShell -STA.'
}

$mutex = New-Object Threading.Mutex($false, 'Local\FortisRdgScriptCatalogV1')
$owned = $false
try {
    try { $owned = $mutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show(
            'Каталог пользовательских скриптов уже открыт.',
            'Control Center'
        ) | Out-Null
        return
    }

    $catalog = Join-Path $PSScriptRoot 'ScriptCatalog.cs'
    $runner = Join-Path $PSScriptRoot 'Script-Runner.ps1'
    foreach ($required in @($catalog, $runner)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Не найден обязательный файл: $required"
        }
    }
    Add-Type -Path $catalog -ReferencedAssemblies @(
        'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll',
        'System.Drawing.dll', 'System.Management.dll', 'System.Xml.dll'
    ) -ErrorAction Stop | Out-Null

    [System.Windows.Forms.Application]::EnableVisualStyles()
    $form = [FortisRdgControl.ScriptCatalogForm]::new($PSScriptRoot)
    [System.Windows.Forms.Application]::Run($form)
} catch {
    $errorPath = Join-Path $PSScriptRoot 'script-catalog-error.log'
    try { [IO.File]::WriteAllText($errorPath, ($_ | Out-String), [Text.Encoding]::UTF8) } catch {}
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show(
            'Не удалось открыть каталог скриптов: ' + $_.Exception.Message +
            "`n`nПодробности: " + $errorPath,
            'Control Center', 'OK', 'Error'
        ) | Out-Null
    } catch {}
    throw
} finally {
    if ($owned) { try { $mutex.ReleaseMutex() } catch {} }
    $mutex.Dispose()
}
