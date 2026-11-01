# Start Docker CI watcher without relying on the old Start-Watcher.ps1 shortcut script,
# which may have been modified to run an interactive `docker exec -it tail -f`.
$ErrorActionPreference = 'Stop'
try {
    $common = Join-Path $env:LOCALAPPDATA 'FortisRDGLauncherV2\Common.ps1'
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
