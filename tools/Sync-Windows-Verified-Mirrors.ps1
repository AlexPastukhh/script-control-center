#requires -Version 5.1
<#
Read-only preview by default. Explicit -Apply backs up uncommitted work in
local Git stash and fast-forwards only three hash-verified Windows clones.
Never drops stashes, resets, cleans, pushes or restarts programs.
Does not touch obs-planning-docs, security, rd-graphs or obs.
#>
[CmdletBinding()]
param([switch]$Apply)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'

$targets = @(
    @{
        Folder = 'ai-conspects'
        Github = 'theory_conspects_and_banks'
        Old = 'c8b6f56a8e27866bd9f3eab5b27f10201b6786c3'
        New = '812ad0817d9f3d1f2ec60f76ae3fa64d4051a700'
    },
    @{
        Folder = 'feature-projection'
        Github = 'f-proj'
        Old = '24af817de8b0cc7a1e4520b06a6c074e4c540a13'
        New = '67add818a1b2ac43557d4f30335e86ac38cb440f'
    },
    @{
        Folder = 'simulation'
        Github = 'simulation'
        Old = '85b6f343e5973bf904209bc19834a321c827792d'
        New = 'd1899aa9ce661cc27f7c6d989bf48ea8875ca6dc'
    }
)

function Get-GitValue {
    param([string]$Repository, [string[]]$GitArguments)
    $old = $env:GIT_OPTIONAL_LOCKS
    $env:GIT_OPTIONAL_LOCKS = '0'
    try {
        $output = @(& git.exe -c core.fsmonitor=false -C $Repository @GitArguments)
        if ($LASTEXITCODE -ne 0) {
            throw "Git read failed: $Repository ($($GitArguments -join ' '))"
        }
        return ($output -join [Environment]::NewLine).Trim()
    } finally {
        $env:GIT_OPTIONAL_LOCKS = $old
    }
}

$plans = @()
foreach ($target in $targets) {
    $path = Join-Path $env:USERPROFILE "Repos\$($target.Folder)"
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Windows directory missing: $path"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $path '.git'))) {
        throw "Not a Git clone: $path"
    }
    $branch = Get-GitValue $path @('symbolic-ref', '--short', 'HEAD')
    $head = Get-GitValue $path @('rev-parse', '--verify', 'HEAD')
    $origin = Get-GitValue $path @('remote', 'get-url', 'origin')
    $originPattern = '^(https://github\.com/|git@github\.com:)AlexPastukhh/' +
        [regex]::Escape($target.Github) + '(\.git)?$'
    if ($branch -ne 'main' -or $head -ne $target.Old -or $origin -notmatch $originPattern) {
        throw "Safety preflight failed: $($target.Folder); branch=$branch; HEAD=$head"
    }
    $dirty = Get-GitValue $path @('status', '--porcelain=v1', '--untracked-files=all')
    $remoteLines = @(& git.exe -c http.sslBackend=openssl -c http.sslVerify=true -C $path ls-remote --exit-code origin refs/heads/main)
    if ($LASTEXITCODE -ne 0 -or $remoteLines.Count -ne 1) {
        throw "Cannot verify GitHub main: $($target.Folder)"
    }
    $remoteSha = ($remoteLines[0] -split '\s+')[0].ToLowerInvariant()
    if ($remoteSha -ne $target.New) {
        throw "GitHub SHA changed: $($target.Folder). Stop without modifications."
    }
    $plans += [pscustomobject]@{
        Name = $target.Folder
        Path = $path
        Old = $target.Old
        New = $target.New
        Dirty = ($dirty.Length -gt 0)
    }
}

Write-Host 'Verified Windows mirrors before changes:'
$plans | Select-Object Name, Dirty, Old, New | Format-Table -AutoSize
if (-not $Apply) {
    Write-Host 'Preview only. No changes. Use -Apply after reviewing running software.'
    return
}

$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
foreach ($plan in $plans) {
    Write-Host ''
    Write-Host ('=== ' + $plan.Name + ' ===')
    if ($plan.Dirty) {
        $message = "Fortis Windows mirror backup before FF $stamp $($plan.Name)"
        & git.exe -C $plan.Path stash push --include-untracked --message $message
        if ($LASTEXITCODE -ne 0) { throw "Stash failed: $($plan.Name). No pull attempted." }
        $afterStash = Get-GitValue $plan.Path @('status', '--porcelain=v1', '--untracked-files=all')
        if ($afterStash.Length -ne 0) {
            throw "Working tree still dirty after stash: $($plan.Name). Stash retained."
        }
        Write-Host "Local stash preserved: $message"
    }
    & git.exe -c http.sslBackend=openssl -c http.sslVerify=true -C $plan.Path fetch --no-tags --no-recurse-submodules origin main
    if ($LASTEXITCODE -ne 0) {
        throw "Fetch failed: $($plan.Name). Stash retained, no merge attempted."
    }
    $fetched = Get-GitValue $plan.Path @('rev-parse', '--verify', 'FETCH_HEAD')
    if ($fetched -ne $plan.New) {
        throw "Fetched SHA changed: $($plan.Name). Stash retained, no merge attempted."
    }
    & git.exe -c core.hooksPath=/dev/null -C $plan.Path merge --ff-only $plan.New
    if ($LASTEXITCODE -ne 0) {
        throw "Fast-forward failed: $($plan.Name). Stash retained."
    }
    $head = Get-GitValue $plan.Path @('rev-parse', '--verify', 'HEAD')
    $status = Get-GitValue $plan.Path @('status', '--porcelain=v1', '--untracked-files=all')
    if ($head -ne $plan.New -or $status.Length -ne 0) {
        throw "Postcheck failed: $($plan.Name). Review status; no automatic undo."
    }
    Write-Host ("Windows mirror updated and clean: $($plan.Name) $($head.Substring(0,8))")
}
Write-Host 'Completed. Prior work remains in local Git stash. No stash was dropped.'
