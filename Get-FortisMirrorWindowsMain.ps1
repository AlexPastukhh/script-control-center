#requires -Version 5.1
<#
Read-only source-of-truth check shared by the two optional Mirror listeners.
Approved code comes from the owner's clean, synchronized Windows main clone,
not from a hardcoded version. No Git update, network access or service start.
#>
Set-StrictMode -Version Latest

function Get-FortisMirrorWindowsMain {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$Root,
        [Parameter(Mandatory=$true)]
        [ValidateSet('OWNER_SHELL', 'MIRROR')][string]$Caller
    )

    $expected = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'Repos\docke-windows-mirror')).TrimEnd('\')
    $actual = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw ($Caller + '_SOURCE_LOCATION_DENIED')
    }
    $source = Get-Item -LiteralPath $actual -Force -ErrorAction Stop
    $gitMeta = Get-Item -LiteralPath (Join-Path $actual '.git') -Force -ErrorAction Stop
    if (-not ($source -is [IO.DirectoryInfo]) -or
        ($source.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not ($gitMeta -is [IO.DirectoryInfo]) -or
        ($gitMeta.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw ($Caller + '_SOURCE_NOT_PRIMARY_CLONE')
    }
    $git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
    $top = @(& $git -C $actual rev-parse --show-toplevel 2>&1)
    if ($LASTEXITCODE -ne 0 -or $top.Count -ne 1 -or
        -not [string]::Equals([IO.Path]::GetFullPath([string]$top[0]).TrimEnd('\'),
            $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw ($Caller + '_SOURCE_ROOT_DENIED')
    }
    $origin = @(& $git -C $actual remote get-url origin 2>&1)
    if ($LASTEXITCODE -ne 0 -or $origin.Count -ne 1 -or
        [string]$origin[0] -notmatch
        '^(?i:https://github\.com/AlexPastukhh/docke-windows-mirror(?:\.git)?|git@github\.com:AlexPastukhh/docke-windows-mirror(?:\.git)?)$') {
        throw ($Caller + '_SOURCE_ORIGIN_DENIED')
    }
    $branch = @(& $git -C $actual symbolic-ref --quiet --short HEAD 2>&1)
    if ($LASTEXITCODE -ne 0 -or $branch.Count -ne 1 -or $branch[0] -cne 'main') {
        throw ($Caller + '_SOURCE_NOT_MAIN')
    }
    $dirty = @(& $git -C $actual status --porcelain=v1 --untracked-files=all 2>&1)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) {
        throw ($Caller + '_SOURCE_DIRTY')
    }
    $head = @(& $git -C $actual rev-parse --verify HEAD 2>&1)
    $headOk = $LASTEXITCODE -eq 0 -and $head.Count -eq 1 -and
        [string]$head[0] -cmatch '^[0-9a-f]{40}$'
    if (-not $headOk) { throw ($Caller + '_SOURCE_HEAD_UNVERIFIED') }
    $upstream = @(& $git -C $actual rev-parse --verify refs/remotes/origin/main 2>&1)
    if ($LASTEXITCODE -ne 0 -or $upstream.Count -ne 1 -or
        [string]$upstream[0] -cnotmatch '^[0-9a-f]{40}$') {
        throw ($Caller + '_SOURCE_UPSTREAM_UNVERIFIED')
    }
    if ([string]$head[0] -cne [string]$upstream[0]) {
        throw ($Caller + '_SOURCE_NOT_SYNCED')
    }
    # The caller may pass this freshly validated commit to its own launcher.
    # It is derived from the local approved origin/main, not a stored pin.
    return [PSCustomObject]@{ Root = $actual; HeadSha = [string]$head[0] }
}
