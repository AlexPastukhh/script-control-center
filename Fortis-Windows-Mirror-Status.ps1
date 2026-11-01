#requires -Version 5.1
<#
Read-only report comparing Windows mirror clones with GitHub main.
No fetch, pull, checkout, commit, push or automatic execution.
Optional -Notify displays a Windows popup; no scheduling is configured.
#>
[CmdletBinding()]
param(
    [string]$MirrorRoot = (Join-Path $env:USERPROFILE 'Repos'),
    [switch]$Notify
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows only.' }
if (-not (Test-Path -LiteralPath $MirrorRoot -PathType Container)) {
    throw "Mirror folder not found: $MirrorRoot"
}
$gitExe = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
function GitRead {
    param([string]$Repository, [string[]]$GitArgs)
    try {
        $text = @(& $gitExe -C $Repository @GitArgs 2>$null)
        return [pscustomobject]@{ Ok = ($LASTEXITCODE -eq 0); Text = (($text | ForEach-Object { [string]$_ }) -join "`n").Trim() }
    } catch {
        return [pscustomobject]@{ Ok = $false; Text = '' }
    }
}
$oldPrompt = $env:GIT_TERMINAL_PROMPT
$oldGcm = $env:GCM_INTERACTIVE
$oldLocks = $env:GIT_OPTIONAL_LOCKS
$rows = New-Object 'System.Collections.Generic.List[object]'
try {
    $env:GIT_TERMINAL_PROMPT = '0'
    $env:GCM_INTERACTIVE = 'Never'
    $env:GIT_OPTIONAL_LOCKS = '0'
    foreach ($dir in @(Get-ChildItem -LiteralPath $MirrorRoot -Directory -Force | Sort-Object Name)) {
        if (($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
        if (-not (Test-Path -LiteralPath (Join-Path $dir.FullName '.git'))) { continue }
        $path = $dir.FullName
        $origin = GitRead $path @('remote','get-url','origin')
        $local = GitRead $path @('rev-parse','--verify','refs/heads/main^{commit}')
        $branch = GitRead $path @('symbolic-ref','--quiet','--short','HEAD')
        $dirty = GitRead $path @('status','--porcelain=v1','--untracked-files=normal')
        $remoteSha = ''
        $state = 'UNKNOWN'
        if (-not $origin.Ok) {
            $state = 'NO ORIGIN'
        } elseif ($origin.Text -notmatch '^(?i:(https://github\.com/|git@github\.com:)AlexPastukhh/[A-Za-z0-9_.-]+(?:\.git)?)$') {
            $state = 'UNEXPECTED ORIGIN'
        } else {
            $remote = GitRead $path @('ls-remote','--exit-code','origin','refs/heads/main')
            if (-not $remote.Ok) {
                $state = 'REMOTE UNAVAILABLE'
            } elseif ($remote.Text -match '(?m)^([0-9a-fA-F]{40})\s+refs/heads/main$') {
                $remoteSha = $Matches[1].ToLowerInvariant()
                if (-not $local.Ok) {
                    $state = 'NO LOCAL MAIN'
                } elseif ($local.Text -eq $remoteSha) {
                    $state = 'CURRENT'
                } else {
                    # Without fetch, different SHA does not prove fast-forward or direction.
                    $state = 'DIFFERENT - REVIEW'
                }
            } else {
                $state = 'UNEXPECTED RESPONSE'
            }
        }
        $rows.Add([pscustomobject]@{
            Repository = $dir.Name
            MainStatus = $state
            LocalMain = if ($local.Ok) { $local.Text.Substring(0,8) } else { '-' }
            GitHubMain = if ($remoteSha) { $remoteSha.Substring(0,8) } else { '-' }
            Branch = if ($branch.Ok) { $branch.Text } else { '(detached)' }
            Changes = if (-not $dirty.Ok) { '?' } elseif ($dirty.Text) { 'YES' } else { 'no' }
        })
    }
} finally {
    $env:GIT_TERMINAL_PROMPT = $oldPrompt
    $env:GCM_INTERACTIVE = $oldGcm
    $env:GIT_OPTIONAL_LOCKS = $oldLocks
}
Write-Host "Windows mirror: $MirrorRoot"
Write-Host 'Checks remote main directly; does not fetch or update local refs.'
if ($rows.Count -eq 0) { Write-Warning 'No Git clones found.'; return }
$rows | Format-Table Repository, MainStatus, LocalMain, GitHubMain, Branch, Changes -AutoSize | Out-Host
$attention = @($rows | Where-Object { $_.MainStatus -ne 'CURRENT' -or $_.Changes -ne 'no' -or $_.Branch -ne 'main' })
Write-Host ("Need review: {0} of {1} clones." -f $attention.Count, $rows.Count)
Write-Host 'DIFFERENT does not necessarily mean behind. Review branch, status and history before any pull.'
if ($Notify -and $attention.Count -gt 0) {
    $names = (@($attention | Select-Object -First 12 -ExpandProperty Repository) -join ', ')
    $message = "Windows Git mirrors requiring review ($($attention.Count)):`n$names`nNo Git changes were made."
    try {
        $shell = New-Object -ComObject WScript.Shell
        [void]$shell.Popup($message, 25, 'Fortis Repo mirror check', 64)
    } catch {
        Write-Warning 'Windows popup could not be shown.'
    }
}
