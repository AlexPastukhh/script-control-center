# Script Center adapter: delegate to the byte-pinned Repo 1 worker in the Windows tunnel clone.
$ErrorActionPreference = 'Stop'
try {
    $resolver = Join-Path $PSScriptRoot 'Get-TunnelWindowsMirror.ps1'
    if (-not (Test-Path -LiteralPath $resolver -PathType Leaf)) { throw "Не найден $resolver" }
    . $resolver
    $mirror = Get-FortisTunnelWindowsMirror -ScriptCenterRoot $PSScriptRoot
    $worker = Join-Path $mirror.Root 'Repo1-Worker.ps1'
    if (-not (Test-Path -LiteralPath $worker -PathType Leaf)) { throw "Не найден $worker" }
    Write-Output ('Запуск Fortis Repo 1 из Windows-клона tunnel; source=' + $mirror.SourceRevision)
    & $worker
    if (-not $?) { throw 'Fortis Repo 1 завершился с ошибкой.' }
} catch {
    [Console]::Error.WriteLine('REPO1 ERROR: ' + $_.Exception.Message)
    exit 1
}
