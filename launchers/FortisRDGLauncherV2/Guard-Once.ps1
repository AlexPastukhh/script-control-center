# Called by optional scheduled task every 2 min. No new containers, no network changes.
. (Join-Path $PSScriptRoot 'Common.ps1')
$logDir = Join-Path $PSScriptRoot 'logs'
function Write-GuardLog([string]$msg) {
    if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $log = Join-Path $logDir 'guard.log'
    if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).Length -gt 512KB) {
        $old = Join-Path $logDir 'guard.previous.log'
        Move-Item -LiteralPath $log -Destination $old -Force
    }
    Add-Content -LiteralPath $log -Value ('{0} {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'),$msg) -Encoding UTF8
}
try {
    if (-not (Get-Command docker.exe -ErrorAction SilentlyContinue)) { return }
    # Docker Desktop may not yet be running. Do not spam the log on ordinary startup.
    $savePref = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $null = & docker.exe info --format '{{.ServerVersion}}' 2>$null
        $ready = ($LASTEXITCODE -eq 0)
    } finally { $ErrorActionPreference = $savePref }
    if (-not $ready) { return }
    $info = Get-FortisInfo
    # Respect manual stop. Docker restart policy, not this task, owns starting containers.
    if (-not $info.State.Running) { return }
    $preview = (@(& $script:GuardPath -Mode Preview) -join "`n") | ConvertFrom-Json
    if ($preview.Preflight -eq 'PASS') { return }
    $id = Ensure-Fortis -DontStart
    Write-GuardLog "Fortis tmpfs guard repaired; ID=$id"
} catch {
    Write-GuardLog "Guard check FAILED: $($_.Exception.Message)"
}
