# RDG deployment watcher; never deploy from a stale ready.json at startup.
# Shared Fortis preparation source is resolved from the sibling Windows tunnel clone.
$ErrorActionPreference = 'Stop'
try {
    $resolver = Join-Path $PSScriptRoot 'Get-TunnelWindowsMirror.ps1'
    if (-not (Test-Path -LiteralPath $resolver -PathType Leaf)) { throw "Не найден $resolver" }
    . $resolver
    $mirror = Get-FortisTunnelWindowsMirror -ScriptCenterRoot $PSScriptRoot
    $prepare = Join-Path $mirror.Root 'launchers\FortisRDGLauncherV2\Start-Fortis.ps1'
    $updater = Join-Path $env:LOCALAPPDATA `
      'RDG-Server\cicd-bootstrap-ci-20261029t051843803595-4546c7d69e\files\RDG-CICD-Bootstrap\Update-RDG.ps1'
    if (-not (Test-Path -LiteralPath $prepare -PathType Leaf)) { throw "Не найден $prepare" }
    if (-not (Test-Path -LiteralPath $updater -PathType Leaf)) { throw "Не найден установленный RDG updater: $updater" }
    & $prepare
    if (-not $?) { throw 'Подготовка Fortis не удалась.' }
    $id = @(& docker.exe inspect --type container --format '{{.Id}}' fortis-repo-test)
    if ($LASTEXITCODE -ne 0 -or @($id).Count -ne 1 -or $id[0] -notmatch '^[0-9a-f]{64}$') {
        throw 'Нельзя получить актуальный ID fortis-repo-test.'
    }
    $raw = @(& docker.exe exec fortis-repo-test cat '/workspace/repo/Workfiles/rdg-ci/ready.json')
    if ($LASTEXITCODE -ne 0 -or $raw.Count -eq 0) { throw 'ready.json не читается.' }
    $ready = ($raw -join "`n") | ConvertFrom-Json
    $health = Invoke-RestMethod -Uri 'http://localhost:18880/healthz' -TimeoutSec 8
    if ($health.status -ne 'ok' -or -not $ready.buildId -or -not $health.buildId) {
        throw 'Состояние готовой и установленной сборки RDG не подтверждено.'
    }
    Write-Output "Готовая сборка: $($ready.buildId)"
    Write-Output "Установленная: $($health.buildId)"
    if ($ready.buildId -cne $health.buildId) {
        throw 'ready.json не совпадает с установленной RDG. Автоматическое развёртывание заблокировано, чтобы не сделать откат. Используй существующий updater отдельно после ручной проверки.'
    }
    Write-Output "RDG updater запущен с текущим Fortis ID $($id[0]). В режиме -Watch ожидает релизы."
    & $updater -Mode Local -AgentContainer $id[0] -Watch
    if (-not $?) { throw 'Updater завершился с ошибкой.' }
} catch {
    [Console]::Error.WriteLine('UPDATER ERROR: ' + $_.Exception.Message)
    exit 1
}
