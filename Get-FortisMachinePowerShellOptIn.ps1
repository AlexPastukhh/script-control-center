#requires -Version 5.1
<#
Owner-only, read-only opt-in source validation. No credentials are created,
printed, regenerated or transmitted. A missing private config means the
existing Script Center entry launches ONLY the legacy owner-only API.
#>
Set-StrictMode -Version Latest

function Get-FortisMachinePowerShellOptIn {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$ConfigPath,
        [Parameter(Mandatory=$true)][string]$ExecutorRoot,
        [Parameter(Mandatory=$true)][string]$ExecutorHeadSha,
        [Parameter(Mandatory=$true)][string]$OwnerSecretFile
    )
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        # Only an explicit pre-provisioned ACL-protected config opts in.
        return @{}
    }
    $expected = [IO.Path]::GetFullPath(
        (Join-Path (Join-Path $env:LOCALAPPDATA 'Fortis\OwnerShell') 'machine-script-center.json'))
    $actual = [IO.Path]::GetFullPath($ConfigPath)
    if (-not [string]::Equals($actual,$expected,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'MACHINE_SHELL_OPTIN_CONFIG_PATH_DENIED'
    }
    # Assert-PrivateFile comes from the pre-existing Script Center adapter.
    $checked = Assert-PrivateFile $actual 'machineOptIn' $true
    try {
        $data = Get-Content -LiteralPath $checked -Raw -Encoding UTF8 |
            ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw 'MACHINE_SHELL_OPTIN_CONFIG_JSON_DENIED'
    }
    $names = @('schemaVersion','enabled','mode','executorHeadSha',
               'machineBearerFile','machineProofFile')
    if ($null -eq $data -or -not ($data -is [PSCustomObject]) -or
            @($data.PSObject.Properties.Name).Count -ne $names.Count -or
            @($names | Where-Object {
                $data.PSObject.Properties.Name -cnotcontains $_
            }).Count -ne 0) {
        throw 'MACHINE_SHELL_OPTIN_SCHEMA_DENIED'
    }
    if ($data.schemaVersion -isnot [int] -or
            $data.schemaVersion -ne 1 -or
            $data.enabled -isnot [bool] -or
            $data.enabled -ne $true -or
            $data.mode -cne 'ASK_ONLY') {
        throw 'MACHINE_SHELL_OPTIN_ASK_ONLY_REQUIRED'
    }
    if ($data.executorHeadSha -isnot [string] -or
            $data.executorHeadSha -cnotmatch '^[0-9a-f]{40}$' -or
            $ExecutorHeadSha -cne $data.executorHeadSha) {
        throw 'MACHINE_SHELL_OPTIN_EXECUTOR_SHA_CHANGED'
    }
    $bearer = [string]$data.machineBearerFile
    $proof = [string]$data.machineProofFile
    if ([string]::IsNullOrWhiteSpace($bearer) -or
            [string]::IsNullOrWhiteSpace($proof) -or
            -not [IO.Path]::IsPathRooted($bearer) -or
            -not [IO.Path]::IsPathRooted($proof)) {
        throw 'MACHINE_SHELL_OPTIN_SECRET_PATH_INVALID'
    }
    $guard = Join-Path $ExecutorRoot 'scripts\Assert-MachineSecretPaths.ps1'
    if (-not (Test-Path -LiteralPath $guard -PathType Leaf)) {
        throw 'MACHINE_SHELL_OPTIN_GUARD_MISSING'
    }
    . $guard
    $guardArgs = @{
        OwnerShellRoot = (Join-Path $env:LOCALAPPDATA 'Fortis\OwnerShell')
        OwnerSecret = $OwnerSecretFile
        MachineBearer = $bearer
        MachineProof = $proof
    }
    Assert-FortisMachineSecretFiles @guardArgs
    # Identity and target are fixed by reviewed source, not owner JSON or AI.
    return @{
        MachineBearerFile = $bearer
        MachineProofFile = $proof
        MachineActor = 'repo-sonar-read-proposal-actor'
        MachineTarget = 'windows-owner'
    }
}
