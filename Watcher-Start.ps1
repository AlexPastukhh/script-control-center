# Start Docker CI watcher without relying on the old Start-Watcher.ps1 shortcut script.
# Shared Fortis code is resolved from the sibling Windows tunnel clone.
$ErrorActionPreference = 'Stop'
try {
    $resolver = Join-Path $PSScriptRoot 'Get-TunnelWindowsMirror.ps1'
    if (-not (Test-Path -LiteralPath $resolver -PathType Leaf)) { throw "Не найден $resolver" }
    . $resolver
    $mirror = Get-FortisTunnelWindowsMirror -ScriptCenterRoot $PSScriptRoot
    $common = Join-Path $mirror.Root 'launchers\FortisRDGLauncherV2\Common.ps1'
    if (-not (Test-Path -LiteralPath $common -PathType Leaf)) { throw "Не найден $common" }
    . $common
    $id = Ensure-Fortis
    Ensure-Watcher
    Write-Output "CI watcher готов; Fortis ID: $id"
    Write-Output 'Updater не запускался. Вывод читается отдельно из watch.log.'
} catch {
    [Console]::Error.WriteLine('WATCHER START ERROR: ' + $_.Exception.Message)
    exit 1
}
