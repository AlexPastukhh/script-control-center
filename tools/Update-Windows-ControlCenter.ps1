#requires -Version 5.1
<#
Owner-operated Windows Git-clone admission for Fortis/RDG Control Center.
Default mode is read-only Preview. -Apply does a fast-forward Git pull and,
only after local read-only preflight, switches the one Desktop shortcut.
No worker, Docker start/stop, Guard Run, profile or secret manipulation.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$RepoPath = (Join-Path $env:USERPROFILE 'repos\script-control-center')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Windows only.' }
$repo = [IO.Path]::GetFullPath($RepoPath)
if (-not (Test-Path -LiteralPath (Join-Path $repo '.git') -PathType Container)) { throw "Git clone not found: $repo" }
$git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
function GitCommand([string[]]$Arguments) {
    $result = @(& $git -C $repo @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Git failed: $($Arguments -join ' ') (exit $LASTEXITCODE)" }
    return $result
}
$remote = [string](GitCommand @('remote','get-url','origin'))
if ($remote -cnotmatch '^https://github\.com/AlexPastukhh/script-control-center(?:\.git)?$') {
    throw "Unexpected Git remote for source checkout: $remote"
}
$branch = [string](GitCommand @('branch','--show-current'))
if ($branch -cne 'main') { throw "Expected checkout main, got: $branch" }
if (@(GitCommand @('status','--porcelain')).Count -ne 0) { throw 'Windows Git clone has local changes: do not overwrite.' }
Write-Host "SOURCE: $repo" -ForegroundColor Cyan
Write-Host ('CURRENT: ' + [string](GitCommand @('rev-parse','--short','HEAD')))
if ($Apply) {
    Write-Host 'Pulling main (fast-forward only; no auto-run)...' -ForegroundColor Cyan
    & $git -C $repo pull --ff-only origin main
    if ($LASTEXITCODE -ne 0) { throw 'Git pull failed; shortcut unchanged.' }
    if (@(GitCommand @('status','--porcelain')).Count -ne 0) { throw 'Git checkout dirty after pull; shortcut unchanged.' }
}
$required = @(
    'Start-ControlCenter.ps1','ControlCenter.cs','ScriptCatalog.cs',
    'Repo1-Worker.ps1','Repo2-Worker.ps1','Updater-Worker.ps1',
    'Watcher-Start.ps1','Watcher-Stop.ps1',
    'launchers\FortisRepoPlus2\Repo2DirectGuard.ps1',
    'launchers\FortisRepoPlus2\FortisRepo2-quiet-logs.ps1',
    'launchers\FortisRDGLauncherV2\Common.ps1',
    'launchers\FortisRDGLauncherV2\Start-Fortis.ps1',
    'launchers\chatgpt-repo-mode\control.ps1',
    'launchers\chatgpt-repo-mode\run-tunnel-quiet-logs.ps1'
)
foreach ($rel in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo $rel) -PathType Leaf)) {
        throw "Missing file in Git clone: $rel"
    }
}
# Resource Guard is pinned; do not auto-replace the pin with an arbitrary new hash.
$guard = Join-Path $repo 'launchers\FortisRepoPlus2\Repo2DirectGuard.ps1'
$repo2 = Join-Path $repo 'launchers\FortisRepoPlus2\FortisRepo2-quiet-logs.ps1'
$hash = (Get-FileHash -LiteralPath $guard -Algorithm SHA256).Hash
$source = [IO.File]::ReadAllText($repo2)
if (-not $source.Contains("'$hash'")) { throw 'Repo 2 guard SHA-256 pin differs from checked-out guard. Shortcut unchanged.' }
# Parse all shipped PowerShell source without executing it.
$toParse = @(Get-ChildItem -LiteralPath (Join-Path $repo 'launchers') -Filter '*.ps1' -File -Recurse) +
    @(Get-ChildItem -LiteralPath $repo -Filter '*.ps1' -File)
foreach ($file in $toParse) {
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    if (@($errors).Count -gt 0) {
        throw "PowerShell syntax error in $($file.FullName): $($errors[0].Message)"
    }
}
Write-Host 'STATIC PREFLIGHT PASS: files, SHA pin, PowerShell syntax.' -ForegroundColor Green
# Compile the real WinForms sources in fresh STA PowerShell processes.
# Add-Type compiles source but does NOT open Control Center or start workers.
if ($Apply) {
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    foreach ($compileScript in @('Test-Compile.ps1','Test-ScriptCatalogCompile.ps1')) {
        $compilePath = Join-Path $repo $compileScript
        if (-not (Test-Path -LiteralPath $compilePath -PathType Leaf)) {
            throw "Windows compile test missing: $compileScript"
        }
        & $windowsPowerShell -NoProfile -STA -ExecutionPolicy Bypass -File $compilePath
        if ($LASTEXITCODE -ne 0) {
            throw "WinForms compile test FAILED: $compileScript. Shortcut unchanged."
        }
    }
    Write-Host 'WINDOWS WINFORMS COMPILATION PASS.' -ForegroundColor Green
}
# -Apply requires read-only acceptance of BOTH cloned launcher source files.
# Preview must never invoke Run or create a second MCP tunnel.
if ($Apply) {
    $repo1PreviewFile = Join-Path $repo 'launchers\chatgpt-repo-mode\run-tunnel-quiet-logs.ps1'
    $repo2PreviewFile = Join-Path $repo 'launchers\FortisRepoPlus2\Repo2DirectGuard.ps1'
    Write-Host 'Testing cloned Repo 1 / Repo 2 guards in Preview (no launch)...' -ForegroundColor Cyan
    $repo1Preview = ((@(& $repo1PreviewFile -Mode Preview) -join "`n") | ConvertFrom-Json)
    $repo2Preview = ((@(& $repo2PreviewFile -Mode Preview) -join "`n") | ConvertFrom-Json)
    Write-Host "Repo1 Preview: $($repo1Preview.Preflight); Repo2 Preview: $($repo2Preview.Preflight)"
    if ($repo1Preview.Preflight -cne 'PASS' -or $repo2Preview.Preflight -cne 'PASS') {
        throw 'Cloned launcher Preview not PASS: shortcut unchanged. Diagnose before cutover.'
    }
}
Write-Host 'Note: WinForms and Repo 1/2 real reconnect NOT tested by this operation.'
$desktop = [Environment]::GetFolderPath('Desktop')
$link = Join-Path $desktop 'Fortis RDG Control Center.lnk'
$target = Join-Path $repo 'Start-ControlCenter.ps1'
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
$shortcut.Description = 'Source: git clone script-control-center; launch is manual'
$shortcut.Save()
Write-Host 'SHORTCUT SWITCHED TO GIT CLONE. Workers and Fortis not started.' -ForegroundColor Green
Write-Host 'Open Control Center, Scripts > Built-in bundle. Manually remove obsolete installed-copy catalog registrations.'
