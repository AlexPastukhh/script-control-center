#requires -Version 5.1
<#
Script Center adapter for the installed Windows FileQuarantine clone.
Runs the project preflight before its GUI, without any interactive pause on
failure (the Script Center captures the error in its log).
Never builds Docker images, creates volumes, clones, pulls or installs anything.
#>
$ErrorActionPreference = 'Stop'
try {
    if ($env:OS -ne 'Windows_NT') { throw 'FILEQ_WINDOWS_ONLY: run on Windows.' }
    $project = Join-Path (Join-Path $env:USERPROFILE 'Repos') 'FileQuarantine'
    $preflight = Join-Path $project '00-Preflight.ps1'
    $gui = Join-Path $project 'FileQuarantine-GUI.ps1'
    if (-not (Test-Path -LiteralPath $preflight -PathType Leaf) -or
        -not (Test-Path -LiteralPath $gui -PathType Leaf)) {
        throw "FILEQ_CLONE_MISSING: expected 00-Preflight.ps1 and FileQuarantine-GUI.ps1 in $project. Install/pull the separate FileQuarantine Git clone."
    }
    $windowsPowerShell = Join-Path $PSHOME 'powershell.exe'
    if (-not (Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) {
        throw 'FILEQ_POWERSHELL_MISSING: Windows PowerShell 5.1 not found.'
    }
    Write-Output "FileQuarantine: checking existing installation at $project"
    & $windowsPowerShell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $preflight
    if ($LASTEXITCODE -ne 0) {
        throw "FILEQ_PREFLIGHT_FAILED: inspect the output above. For missing Docker images run Setup-Project.ps1 -Apply manually from $project (do not initialize or delete existing volumes)."
    }
    Write-Output 'FileQuarantine: starting GUI (Windows PowerShell STA).'
    & $windowsPowerShell -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File $gui
    if ($LASTEXITCODE -ne 0) { throw "FILEQ_GUI_FAILED: exit code $LASTEXITCODE" }
} catch {
    [Console]::Error.WriteLine('FILEQUARANTINE_LAUNCH_ERROR: ' + $_.Exception.Message)
    exit 1
}
