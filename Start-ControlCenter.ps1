# Windows PowerShell 5.1; native WinForms UI, built locally by Add-Type.
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Only Windows is supported.' }
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Start with Windows PowerShell -STA (the desktop shortcut does this).'
}
$mutex = New-Object Threading.Mutex($false, 'Local\FortisRdgControlCenterV1')
$owned = $false
try {
    try { $owned = $mutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show('Control Center уже открыт. Найди его окно или значок в трее.', 'Fortis / RDG') | Out-Null
        return
    }
    $source = Join-Path $PSScriptRoot 'ControlCenter.cs'
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'ControlCenter.cs не найден.' }
    Add-Type -Path @($source, (Join-Path $PSScriptRoot 'ScriptCatalog.cs')) -ReferencedAssemblies @(
      'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll',
      'System.Drawing.dll', 'System.Management.dll', 'System.Xml.dll'
    ) -ErrorAction Stop | Out-Null
    [FortisRdgControl.MainForm]::Run($PSScriptRoot)
} catch {
    $errorPath = Join-Path $PSScriptRoot 'launch-error.log'
    try {
        [IO.File]::WriteAllText($errorPath, ($_ | Out-String), [Text.Encoding]::UTF8)
    } catch { }
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show(
            'Не удалось открыть Fortis/RDG Control Center: ' + $_.Exception.Message + "`n`nПодробности: " + $errorPath,
            'Ошибка приложения', 'OK', 'Error'
        ) | Out-Null
    } catch { }
    throw
} finally {
    if ($owned) { try { $mutex.ReleaseMutex() } catch {} }
    $mutex.Dispose()
}
