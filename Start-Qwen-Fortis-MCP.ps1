#requires -Version 5.1
<# Script Catalog Qwen -> SSE mcp-proxy -> existing Fortis Repo Desktop Commander.
The uv tool runner may download mcp-proxy on its first invocation.
Register only in mode window: visible proxy console, no automatic startup.
#>
[CmdletBinding()]
param([ValidateRange(1024,65535)][int]$Port=18765)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
try {
    if($env:OS -cne 'Windows_NT') { throw 'QWEN_WINDOWS_REQUIRED' }
    $python=(Get-Command py.exe -CommandType Application -ErrorAction Stop).Source
    $docker=(Get-Command docker.exe -CommandType Application -ErrorAction Stop).Source
    $uv=@(& $python -m uv --version 2>&1)
    if($LASTEXITCODE -ne 0 -or $uv.Count -eq 0) { throw 'QWEN_UV_UNAVAILABLE: verify py -m uv --version.' }
    $running=(& $docker inspect --type container --format '{{.State.Running}}' fortis-repo-test 2>$null | Out-String).Trim()
    if($LASTEXITCODE -ne 0 -or $running -cne 'true') { throw 'QWEN_FORTIS_CONTAINER_NOT_RUNNING: check the existing container manually.' }
    if(@(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue).Count -gt 0) {
        throw ('QWEN_PROXY_PORT_BUSY: TCP '+$Port+' already has a listener. Stop the old proxy manually first.')
    }
    Write-Host ('Qwen MCP SSE: http://127.0.0.1:'+$Port+'/sse')
    Write-Host 'Leave this window open; Ctrl+C stops this proxy, not the Docker container.'
    Write-Host 'MCP has read/write/process permissions on the shared volume as UID 1000.'
    $proxyArgs=@('-m','uv','tool','run','mcp-proxy','--host','127.0.0.1','--port',[string]$Port,'--',
        $docker,'exec','-i','--user','1000:1000','--env','HOME=/workspace/files/Qwen-Fortis-MCP/container-home',
        'fortis-repo-test','/usr/local/bin/node','/opt/dc/node_modules/@wonderwhy-er/desktop-commander/dist/index.js')
    & $python @proxyArgs
    if($LASTEXITCODE -ne 0) { throw ('QWEN_PROXY_EXIT_NONZERO: '+$LASTEXITCODE) }
} catch {
    [Console]::Error.WriteLine('QWEN_FORTIS_MCP_LAUNCH_ERROR: '+$_.Exception.Message)
    exit 1
}
