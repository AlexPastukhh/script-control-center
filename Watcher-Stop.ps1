# Exact PID matching, copied in purpose from the established v2 Stop-Watcher script.
# No interactive prompt here: the GUI requests confirmation first.
$ErrorActionPreference = 'Stop'
try {
    $common = Join-Path $env:LOCALAPPDATA 'FortisRDGLauncherV2\Common.ps1'
    if (-not (Test-Path -LiteralPath $common -PathType Leaf)) { throw "Не найден $common" }
    . $common
    Assert-Docker
    $info = Get-FortisInfo
    if (-not $info.State.Running) { Write-Output 'Fortis stopped; watcher not running.'; return }
    Assert-FortisSecurity $info
    $matches = @(Get-WatcherMatches)
    if ($matches.Count -gt 1) { throw 'Найдено больше одного CI watcher. Остановка отменена.' }
    if ($matches.Count -eq 0) { Write-Output 'CI watcher уже остановлен.'; return }
    $code = @'
const fs=require('fs'),path=require('path');
const found=[];
for (const n of fs.readdirSync('/proc')) {
  if (!/^\d+$/.test(n)) continue;
  let a;
  try { a=fs.readFileSync(`/proc/${n}/cmdline`).toString().split('\0').filter(Boolean); } catch { continue; }
  if (!a.length || !/^python(?:3(?:\.\d+)?)?$/.test(path.basename(a[0]))) continue;
  if (a.includes('-B') && a.includes('tools/rdg-ci.py') && a.includes('--watch')) found.push(Number(n));
}
if(found.length>1) { console.error('Multiple watchers; refusing'); process.exit(3); }
if(!found.length) { console.log('Watcher already stopped'); process.exit(0); }
process.kill(found[0], 'SIGTERM');
console.log('SIGTERM sent to CI watcher PID', found[0]);
'@
    & docker.exe exec fortis-repo-test /usr/local/bin/node -e $code
    if ($LASTEXITCODE -ne 0) { throw 'SIGTERM watcher завершился с ошибкой.' }
    Write-Output 'Остановлен только CI watcher. RDG и Fortis не затронуты.'
} catch {
    [Console]::Error.WriteLine('WATCHER STOP ERROR: ' + $_.Exception.Message)
    exit 1
}
