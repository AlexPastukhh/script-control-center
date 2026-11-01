# Script Center adapter: delegate to the byte-pinned Repo 2 worker in the Windows tunnel clone.
$ErrorActionPreference = 'Stop'
try {
    $resolver = Join-Path $PSScriptRoot 'Get-TunnelWindowsMirror.ps1'
    if (-not (Test-Path -LiteralPath $resolver -PathType Leaf)) { throw "Не найден $resolver" }
    . $resolver
    $mirror = Get-FortisTunnelWindowsMirror -ScriptCenterRoot $PSScriptRoot
    $worker = Join-Path $mirror.Root 'Repo2-Worker.ps1'
    if (-not (Test-Path -LiteralPath $worker -PathType Leaf)) { throw "Не найден $worker" }
    Write-Output ('Запуск Fortis Repo 2 из Windows-клона tunnel; source=' + $mirror.SourceRevision)
    & $worker
    if (-not $?) { throw 'Fortis Repo 2 завершился с ошибкой.' }
} catch {
    [Console]::Error.WriteLine('REPO2 ERROR: ' + $_.Exception.Message)
    exit 1
}
