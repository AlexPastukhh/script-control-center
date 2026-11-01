#requires -Version 5.1
<#
Owner-operated paired Git-clone admission for Fortis/RDG Control Center + Tunnel.
Default mode is read-only Preview. -Apply fast-forwards tunnel first, then Script Center,
validates the byte-pinned Windows tunnel mirror, compiles WinForms and runs read-only
Repo1/Repo2 Previews before repairing the one Desktop shortcut. No worker or service starts.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$RepoPath = (Join-Path $env:USERPROFILE 'Repos\script-control-center'),
    [string]$TunnelRepoPath = (Join-Path $env:USERPROFILE 'Repos\tunnel')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Windows only.' }
$expectedRepo = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'Repos\script-control-center')).TrimEnd('\')
$expectedTunnel = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'Repos\tunnel')).TrimEnd('\')
$repo = [IO.Path]::GetFullPath($RepoPath).TrimEnd('\')
$tunnelRepo = [IO.Path]::GetFullPath($TunnelRepoPath).TrimEnd('\')
if (-not [string]::Equals($repo,$expectedRepo,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Script Center must use the canonical %USERPROFILE%\Repos\script-control-center clone.'
}
if (-not [string]::Equals($tunnelRepo,$expectedTunnel,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Tunnel must use the sibling canonical %USERPROFILE%\Repos\tunnel clone.'
}
$git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
function GitCommand([string]$Root,[string[]]$Arguments) {
    $result = @(& $git -C $Root @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Git failed in ${Root}: $($Arguments -join ' ') (exit $LASTEXITCODE)" }
    return $result
}
function Assert-GitClone([string]$Root,[string]$ExpectedRemote,[string]$Name) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root '.git'))) { throw "$Name Git clone not found: $Root" }
    $remote = ([string](GitCommand $Root @('remote','get-url','origin'))).Trim()
    if ($remote -cnotmatch $ExpectedRemote) { throw "$Name has an unexpected origin: $remote" }
    $branch = ([string](GitCommand $Root @('branch','--show-current'))).Trim()
    if ($branch -cne 'main') { throw "$Name must be on main; found '$branch'." }
    if (@(GitCommand $Root @('status','--porcelain','--untracked-files=normal')).Count -ne 0) {
        throw "$Name has local/untracked changes; refusing to overwrite: $Root"
    }
    Write-Host ("{0}: {1} @ {2} (clean main)" -f $Name,$Root,([string](GitCommand $Root @('rev-parse','--short','HEAD'))).Trim()) -ForegroundColor Cyan
}

# Preflight both clones before either pull. No stash/reset/clean/checkout is used.
Assert-GitClone $tunnelRepo '^https://github\.com/AlexPastukhh/tunnel(?:\.git)?$' 'Tunnel'
Assert-GitClone $repo '^https://github\.com/AlexPastukhh/script-control-center(?:\.git)?$' 'Script Center'
if ($Apply) {
    Write-Host 'Pulling Tunnel main first (fast-forward only)...' -ForegroundColor Cyan
    & $git -C $tunnelRepo pull --ff-only origin main
    if ($LASTEXITCODE -ne 0) { throw 'Tunnel pull failed. Script Center and shortcut were not changed.' }
    if (@(GitCommand $tunnelRepo @('status','--porcelain','--untracked-files=normal')).Count -ne 0) {
        throw 'Tunnel checkout is not clean after pull.'
    }
    Write-Host 'Pulling Script Center main (fast-forward only)...' -ForegroundColor Cyan
    & $git -C $repo pull --ff-only origin main
    if ($LASTEXITCODE -ne 0) { throw 'Script Center pull failed. No shortcut or worker was changed.' }
    if (@(GitCommand $repo @('status','--porcelain','--untracked-files=normal')).Count -ne 0) {
        throw 'Script Center checkout is not clean after pull.'
    }
}

$required = @(
    'Start-ScriptCatalog.ps1','Start-ControlCenter.ps1','ControlCenter.cs','ScriptCatalog.cs',
    'Get-TunnelWindowsMirror.ps1','Repo1-Worker.ps1','Repo2-Worker.ps1','Updater-Worker.ps1',
    'Watcher-Start.ps1','Watcher-Stop.ps1','Test-Compile.ps1','Test-ScriptCatalogCompile.ps1'
)
foreach ($rel in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo $rel) -PathType Leaf)) {
        throw "Missing file in Script Center Git clone: $rel"
    }
}
$resolver = Join-Path $repo 'Get-TunnelWindowsMirror.ps1'
. $resolver
$mirror = Get-FortisTunnelWindowsMirror -ScriptCenterRoot $repo
Write-Host ("TUNNEL MIRROR PASS: {0}; manifest={1}" -f $mirror.Root,$mirror.ManifestSha256) -ForegroundColor Green

# Verify the adjacent Repo2 Guard pin, then parse every active adapter and mirrored launcher.
$guard = Join-Path $mirror.Root 'launchers\FortisRepoPlus2\Repo2DirectGuard.ps1'
$repo2 = Join-Path $mirror.Root 'launchers\FortisRepoPlus2\FortisRepo2-quiet-logs.ps1'
$hash = (Get-FileHash -LiteralPath $guard -Algorithm SHA256).Hash
$source = [IO.File]::ReadAllText($repo2)
if (-not $source.Contains("'$hash'")) { throw 'Repo 2 guard SHA-256 pin differs from mirrored guard.' }
$toParse = @(Get-ChildItem -LiteralPath $repo -Filter '*.ps1' -File) +
    @(Get-ChildItem -LiteralPath (Join-Path $repo 'tools') -Filter '*.ps1' -File) +
    @(Get-ChildItem -LiteralPath $mirror.Root -Filter '*.ps1' -File -Recurse)
foreach ($file in $toParse) {
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    if (@($errors).Count -gt 0) {
        throw "PowerShell syntax error in $($file.FullName): $($errors[0].Message)"
    }
}
Write-Host 'STATIC PREFLIGHT PASS: paired Git clones, mirror hashes, Guard pin, PowerShell syntax.' -ForegroundColor Green

# Compile source only; this does not open the UI or start any worker.
if ($Apply) {
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    foreach ($compileScript in @('Test-Compile.ps1','Test-ScriptCatalogCompile.ps1')) {
        $compilePath = Join-Path $repo $compileScript
        & $windowsPowerShell -NoProfile -STA -ExecutionPolicy Bypass -File $compilePath
        if ($LASTEXITCODE -ne 0) { throw "WinForms compile test FAILED: $compileScript. Shortcut unchanged." }
    }
    Write-Host 'WINDOWS WINFORMS COMPILATION PASS.' -ForegroundColor Green

    # Read-only Preview uses the exact source files from the Windows tunnel clone.
    $repo1PreviewFile = Join-Path $mirror.Root 'launchers\chatgpt-repo-mode\run-tunnel-quiet-logs.ps1'
    $repo2PreviewFile = Join-Path $mirror.Root 'launchers\FortisRepoPlus2\Repo2DirectGuard.ps1'
    Write-Host 'Testing mirrored Repo 1 / Repo 2 guards in Preview (no launch)...' -ForegroundColor Cyan
    $repo1Preview = ((@(& $repo1PreviewFile -Mode Preview) -join "`n") | ConvertFrom-Json)
    $repo2Preview = ((@(& $repo2PreviewFile -Mode Preview) -join "`n") | ConvertFrom-Json)
    Write-Host "Repo1 Preview: $($repo1Preview.Preflight); Repo2 Preview: $($repo2Preview.Preflight)"
    if ($repo1Preview.Preflight -cne 'PASS' -or $repo2Preview.Preflight -cne 'PASS') {
        throw 'Mirrored launcher Preview not PASS. Shortcut unchanged; diagnose before cutover.'
    }
    if (@(GitCommand $tunnelRepo @('status','--porcelain','--untracked-files=normal')).Count -ne 0 -or
        @(GitCommand $repo @('status','--porcelain','--untracked-files=normal')).Count -ne 0) {
        throw 'A read-only preflight unexpectedly changed a Git checkout.'
    }
}
Write-Host 'Note: live Repo 1/2 reconnection is NOT tested by this operation.'
$desktop = [Environment]::GetFolderPath('Desktop')
$link = Join-Path $desktop 'Fortis RDG Control Center.lnk'
$target = Join-Path $repo 'Start-ScriptCatalog.ps1'
Write-Host "DESKTOP SHORTCUT: $link"
Write-Host "TARGET SOURCE: $target"
if (-not $Apply) {
    Write-Host 'PREVIEW ONLY: No Git pull, shortcut changes, worker starts or container changes.'
    return
}
$ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $ps -PathType Leaf)) { throw 'Windows PowerShell 5.1 not found.' }
if (Test-Path -LiteralPath $link -PathType Leaf) {
    $backup = $link + '.before-git-clone-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak'
    Copy-Item -LiteralPath $link -Destination $backup -ErrorAction Stop
    Write-Host "SHORTCUT BACKUP: $backup"
}
$ws = New-Object -ComObject WScript.Shell
$shortcut = $ws.CreateShortcut($link)
$shortcut.TargetPath = $ps
$shortcut.Arguments = '-NoProfile -STA -ExecutionPolicy Bypass -File "' + $target + '"'
$shortcut.WorkingDirectory = $repo
$shortcut.WindowStyle = 1
$shortcut.Description = 'Standalone Script Catalog from Git clone; launch is manual'
$shortcut.Save()
Write-Host 'SHORTCUT SWITCHED TO GIT CLONE. Workers and Fortis not started.' -ForegroundColor Green
Write-Host 'Existing catalog registrations are preserved; do not reimport on routine pulls.'
