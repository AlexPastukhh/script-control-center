# Resolve the one approved Windows source mirror for all tunnel-related Script Center workers.
# This helper is dot-sourced by worker adapters; it never copies or installs source files.
function Get-FortisTunnelWindowsMirror {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)][string]$ScriptCenterRoot)

    $ErrorActionPreference = 'Stop'
    if ($env:OS -ne 'Windows_NT') { throw 'Windows PowerShell is required.' }

    $expectedScriptCenter = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'Repos\script-control-center')).TrimEnd('\')
    $actualScriptCenter = [IO.Path]::GetFullPath($ScriptCenterRoot).TrimEnd('\')
    if (-not [string]::Equals($actualScriptCenter,$expectedScriptCenter,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Script Center must run from %USERPROFILE%\Repos\script-control-center.'
    }

    $reposRoot = Split-Path -Parent $expectedScriptCenter
    $tunnelRoot = [IO.Path]::GetFullPath((Join-Path $reposRoot 'tunnel')).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $tunnelRoot -PathType Container)) {
        throw "Windows tunnel Git clone is missing: $tunnelRoot. Clone/pull both repositories first."
    }
    $git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
    if (-not (Test-Path -LiteralPath (Join-Path $expectedScriptCenter '.git'))) {
        throw 'Script Center is not the expected Windows Git clone.'
    }
    $scriptCenterRemote = @(& $git -C $expectedScriptCenter remote get-url origin 2>&1)
    if ($LASTEXITCODE -ne 0 -or $scriptCenterRemote.Count -ne 1 -or
        [string]$scriptCenterRemote[0] -cnotmatch '^https://github\.com/AlexPastukhh/script-control-center(?:\.git)?$') {
        throw 'Windows Script Center clone has an unexpected origin.'
    }
    $scriptCenterBranch = @(& $git -C $expectedScriptCenter branch --show-current 2>&1)
    if ($LASTEXITCODE -ne 0 -or $scriptCenterBranch.Count -ne 1 -or $scriptCenterBranch[0] -cne 'main') {
        throw 'Windows Script Center clone must be on main.'
    }
    $scriptCenterDirty = @(& $git -C $expectedScriptCenter status --porcelain --untracked-files=normal 2>&1)
    if ($LASTEXITCODE -ne 0 -or $scriptCenterDirty.Count -ne 0) {
        throw 'Windows Script Center clone must be clean; refusing unreviewed local source.'
    }
    $scriptCenterSync = @(& $git -C $expectedScriptCenter rev-list --left-right --count 'HEAD...origin/main' 2>&1)
    if ($LASTEXITCODE -ne 0 -or $scriptCenterSync.Count -ne 1 -or
        ([string]$scriptCenterSync[0]).Trim() -notmatch '^0\s+0$') {
        throw 'Windows Script Center main must be synchronized with its fetched origin/main. Run the paired updater.'
    }

    $remote = @(& $git -C $tunnelRoot remote get-url origin 2>&1)
    if ($LASTEXITCODE -ne 0 -or $remote.Count -ne 1 -or
        [string]$remote[0] -cnotmatch '^https://github\.com/AlexPastukhh/tunnel(?:\.git)?$') {
        throw 'Windows tunnel clone has an unexpected origin.'
    }
    $branch = @(& $git -C $tunnelRoot branch --show-current 2>&1)
    if ($LASTEXITCODE -ne 0 -or $branch.Count -ne 1 -or $branch[0] -cne 'main') {
        throw 'Windows tunnel clone must be on main.'
    }
    $dirty = @(& $git -C $tunnelRoot status --porcelain --untracked-files=normal 2>&1)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) {
        throw 'Windows tunnel clone must be clean; refusing unreviewed local source.'
    }
    $tunnelSync = @(& $git -C $tunnelRoot rev-list --left-right --count 'HEAD...origin/main' 2>&1)
    if ($LASTEXITCODE -ne 0 -or $tunnelSync.Count -ne 1 -or
        ([string]$tunnelSync[0]).Trim() -notmatch '^0\s+0$') {
        throw 'Windows tunnel main must be synchronized with its fetched origin/main. Run the paired updater.'
    }

    $currentRoot = Join-Path $tunnelRoot 'runtime\windows-repo\current'
    $manifestPath = Join-Path $currentRoot 'source-manifest.json'
    $expectedManifestHash = '45EEF5FB15B8AD3873C7939EF25A32D75137DCDB7CDD81F6CD0EDAE94027CD36'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -cne $expectedManifestHash) {
        throw 'Tunnel Windows source manifest is missing or has an unexpected fingerprint. Pull the reviewed tunnel main.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $expectedFiles = @(
        'Repo1-Worker.ps1',
        'Repo2-Worker.ps1',
        'launchers/chatgpt-repo-mode/control.ps1',
        'launchers/chatgpt-repo-mode/run-tunnel-quiet-logs.ps1',
        'launchers/FortisRDGLauncherV2/Common.ps1',
        'launchers/FortisRDGLauncherV2/Start-Fortis.ps1',
        'launchers/FortisRepoPlus2/Repo2DirectGuard.ps1',
        'launchers/FortisRepoPlus2/FortisRepo2-quiet-logs.ps1',
        'launchers/shared/FortisContainerPolicy.ps1'
    )
    if ($manifest.schema_version -ne 1 -or
        $manifest.status -cne 'TUNNEL_CANONICAL_SHARED_GUARD_POLICY_GO3C' -or
        $manifest.source_repository -cne 'tunnel' -or
        $manifest.source_ref -cne 'tunnel:main' -or
        $manifest.source_revision_type -cne 'sha256_file_inventory_v1' -or
        $manifest.source_revision -cne '009D9FEBE34DBB952426219C10F969605F410FE37D902F1B0A5FB3292A10D6F7' -or
        @($manifest.files).Count -ne $expectedFiles.Count) {
        throw 'Unexpected tunnel Windows source manifest. Nothing started.'
    }
    foreach ($rel in $expectedFiles) {
        $entries = @($manifest.files | Where-Object { $_.path -ceq $rel })
        if ($entries.Count -ne 1 -or ([string]$entries[0].sha256) -cnotmatch '^[0-9A-F]{64}$') {
            throw ('Missing or invalid tunnel source hash: ' + $rel)
        }
        $file = Join-Path $currentRoot ($rel.Replace('/','\'))
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or
            (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -cne $entries[0].sha256) {
            throw ('Tunnel source hash mismatch: ' + $rel)
        }
    }
    return [PSCustomObject]@{
        TunnelRepo = $tunnelRoot
        Root = $currentRoot
        ManifestPath = $manifestPath
        ManifestSha256 = $expectedManifestHash
        SourceRevision = [string]$manifest.source_revision
    }
}
