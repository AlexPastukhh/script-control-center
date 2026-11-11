#requires -Version 5.1
<#
Script Catalog adapter for the existing opt-in Owner PowerShell Executor.
Does not provision credentials, create a receipt DB, or deploy Gateway.
Import as window (Своё окно) only; never run as Administrator.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-PrivateFile {
    param([string]$Value, [string]$Label, [bool]$Private)
    if ([string]::IsNullOrWhiteSpace($Value) -or -not [IO.Path]::IsPathRooted($Value)) {
        throw ('OWNER_SHELL_BAD_PATH_' + $Label)
    }
    $path = [IO.Path]::GetFullPath($Value)
    $prefix = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\') + '\'
    if (-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) {
        throw ('OWNER_SHELL_OUTSIDE_LOCALAPPDATA_' + $Label)
    }
    $file = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if (-not ($file -is [IO.FileInfo]) -or
        ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw ('OWNER_SHELL_UNSAFE_FILE_' + $Label)
    }
    if ($Private) {
        $acl = Get-Acl -LiteralPath $path
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $allowed = @($sid,'S-1-5-18','S-1-5-32-544')
        if (-not $acl.AreAccessRulesProtected -or
            $allowed -cnotcontains $acl.GetOwner(
                [Security.Principal.SecurityIdentifier]).Value) {
            throw ('OWNER_SHELL_ACL_' + $Label)
        }
        foreach ($rule in $acl.GetAccessRules(
                $true,$true,[Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -eq 'Allow' -and
                $allowed -cnotcontains $rule.IdentityReference.Value) {
                throw ('OWNER_SHELL_ACL_' + $Label)
            }
        }
    }
    return $path
}

try {
    if ($env:OS -cne 'Windows_NT') { throw 'OWNER_SHELL_WINDOWS_ONLY' }
    $user = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ([Security.Principal.WindowsPrincipal]::new($user).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'OWNER_SHELL_ADMIN_DENIED'
    }
    $repo = Join-Path $env:USERPROFILE 'Repos\docke-windows-mirror'
    $launcher = Join-Path $repo 'Start-OwnerPowerShellExecutor.ps1'
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        throw 'OWNER_SHELL_LAUNCHER_MISSING'
    }
    $git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
    $null = Get-Command py.exe -CommandType Application -ErrorAction Stop
    $branch = (& $git -C $repo branch --show-current | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -cne 'main') { throw 'OWNER_SHELL_SOURCE_NOT_MAIN' }
    # Preserve the reviewed Mirror base and only permit the documented
    # Gateway READ runbook update. Other Mirror changes require new review.
    $approvedBase = 'a5cb3e9f61e68331c11337eb5da4af056d27660c'
    $sha = (& $git -C $repo rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sha -cnotmatch '^[0-9a-f]{40}$') {
        throw 'OWNER_SHELL_SOURCE_HEAD_INVALID'
    }
    & $git -C $repo merge-base --is-ancestor $approvedBase $sha | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'OWNER_SHELL_SOURCE_BASE_MISMATCH' }
    $changed = @(& $git -C $repo diff --name-only --no-ext-diff $approvedBase $sha)
    if ($LASTEXITCODE -ne 0 -or
        @($changed | Where-Object { $_ -cne 'docs/GATEWAY-READ-ONLY.md' }).Count -ne 0) {
        throw 'OWNER_SHELL_SOURCE_DIFF_DENIED'
    }
    # Ancestry/diff is the main guard; pin the executable source objects too.
    $sourceBlobs = [ordered]@{
        'Start-OwnerPowerShellExecutor.ps1' = '36798dbc1c48829eb395b08128618ac3c8962b57'
        'mirror_api' = '390ad3cd7938364a5861cb9d3ef707f31d7037b9'
    }
    foreach ($rel in $sourceBlobs.Keys) {
        $actual = (& $git -C $repo rev-parse --verify ('HEAD:' + $rel) | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $actual -cne $sourceBlobs[$rel]) {
            throw 'OWNER_SHELL_SOURCE_BLOB_MISMATCH'
        }
    }
    $dirty = @(& $git -C $repo status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) {
        throw 'OWNER_SHELL_SOURCE_DIRTY'
    }
    $origin = (& $git -C $repo remote get-url origin | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $origin -notmatch
        '^(?i:https://github\.com/AlexPastukhh/docke-windows-mirror(?:\.git)?|git@github\.com:AlexPastukhh/docke-windows-mirror(?:\.git)?)$') {
        throw 'OWNER_SHELL_SOURCE_ORIGIN_INVALID'
    }

    $configPath = Join-Path $env:LOCALAPPDATA 'Fortis\OwnerShell\script-center-launch.json'
    $names = @('serverCert','serverKey','clientCA','secretFile','receiptDb')
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        if (@(Get-NetTCPConnection -LocalPort 10443 -State Listen -ErrorAction SilentlyContinue).Count -ne 0) {
            throw 'OWNER_SHELL_PORT_IN_USE_DO_NOT_DUPLICATE'
        }
        Write-Host 'ONE-TIME SETUP: enter only file PATHS, not key or token contents.'
        Write-Host 'Use the SAME EXISTING SQLite receipt database, never a new empty DB.'
        $settings = [ordered]@{}
        foreach ($name in $names) {
            $inputPath = Read-Host ('Existing absolute LOCALAPPDATA file path for ' + $name)
            $settings[$name] = Assert-PrivateFile $inputPath $name (
                $name -in @('serverKey','secretFile','receiptDb'))
        }
        $directory = Split-Path -Parent $configPath
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            $null = New-Item -ItemType Directory -Path $directory -Force
        }
        $temp = Join-Path $directory ('setup-' + [guid]::NewGuid().ToString('N') + '.tmp')
        try {
            [IO.File]::WriteAllText($temp,($settings | ConvertTo-Json -Depth 3),
                                    [Text.UTF8Encoding]::new($false))
            $acl = Get-Acl -LiteralPath $temp
            $acl.SetAccessRuleProtection($true,$false)
            $acl.SetOwner($user.User)
            $acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                $user.User,[Security.AccessControl.FileSystemRights]::FullControl,
                [Security.AccessControl.AccessControlType]::Allow))
            Set-Acl -LiteralPath $temp -AclObject $acl -ErrorAction Stop
            if (Test-Path -LiteralPath $configPath) { throw 'OWNER_SHELL_CONFIG_ALREADY_EXISTS' }
            Move-Item -LiteralPath $temp -Destination $configPath -ErrorAction Stop
        } finally {
            if (Test-Path -LiteralPath $temp) {
                Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
            }
        }
        Write-Host 'OWNER_SHELL_PATHS_CONFIGURED=YES'
        Write-Host 'OWNER_SHELL_STARTED=NO'
        Write-Host 'Review local path-only config; click Start again to launch.'
        return
    }
    $config = Assert-PrivateFile $configPath 'config' $true
    $data = Get-Content -LiteralPath $config -Raw -Encoding UTF8 |
        ConvertFrom-Json -ErrorAction Stop
    if (@($data.PSObject.Properties.Name).Count -ne $names.Count -or
        @($names | Where-Object { $data.PSObject.Properties.Name -cnotcontains $_ }).Count -ne 0) {
        throw 'OWNER_SHELL_CONFIG_SCHEMA_INVALID'
    }
    $paths = @{}
    foreach ($name in $names) {
        $paths[$name] = Assert-PrivateFile ([string]$data.$name) $name (
            $name -in @('serverKey','secretFile','receiptDb'))
    }
    if (@(Get-NetTCPConnection -LocalPort 10443 -State Listen -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'OWNER_SHELL_PORT_IN_USE_DO_NOT_DUPLICATE'
    }
    Write-Host 'OWNER_SHELL_SCRIPT_CENTER_PREFLIGHT_PASS'
    Write-Host 'Launching existing Windows Executor on 127.0.0.1:10443.'
    Write-Host 'Authenticated owner scripts run as this Windows user. Ctrl+C stops this instance.'
    & $launcher -Bind '127.0.0.1' -Port 10443 -ServerCert $paths.serverCert -ServerKey $paths.serverKey -ClientCA $paths.clientCA -SecretFile $paths.secretFile -ReceiptDb $paths.receiptDb
    if (-not $?) { throw 'OWNER_SHELL_EXECUTOR_FAILED' }
}
catch {
    [Console]::Error.WriteLine('OWNER_SHELL_SCRIPT_CENTER_STOP: ' + $_.Exception.Message)
    exit 1
}
