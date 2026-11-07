#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

# GO-3 rollback only. Deletes the recorded replacement CONTAINER only.
# No -v flag is used. No volume/network is deleted or recreated. R7 is not restarted.

$CutoverId = '20261102-144258'
$OldName = 'fortis-repo-test'
$RollbackName = 'fortis-repo-test-go3-rollback-20261102-144258'
$OldContainerId = '0c0f8cb2d608679b4c3de51c527cbe2aca687cbf1360e6bf116444b2a0928cc2'
$ImageId = 'sha256:2c84fabebb34265e53b020f21a11683a4c68cbb41a08f5f11f9b01c7de5ed8c9'
$StatePath = Join-Path $env:LOCALAPPDATA 'Fortis\GO3-Cutover\20261102-144258\go3-cutover-state.json'
$EgressNetworkId = '65a8a1d816e3c6e5703d976d31514ecd657dfb14f5e5fafaba0e5208046e3ffd'
$InternalNetworkId = 'a4b5b7e37facafe180b5bb572ea3d228e3349b77f6babb760452708082566524'
$R7ImageId = 'sha256:23667619c6dfac33466d0e0b4b60bcb22fb7dccb14b0a024f2c062bac0a18ce4'

if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
    throw 'STOP: Windows PowerShell 5.1 required'
}
$dockerCommand = Get-Command docker.exe -CommandType Application | Select-Object -First 1
if ($null -eq $dockerCommand -or -not (Test-Path -LiteralPath $dockerCommand.Source -PathType Leaf)) {
    throw 'STOP: docker.exe not found'
}
$script:DockerExe = $dockerCommand.Source

function Invoke-DockerText {
    param([string[]]$Arguments)
    $lines=@(& $script:DockerExe @Arguments 2>&1)
    $exitCode=$LASTEXITCODE
    $text=($lines | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($exitCode -ne 0) { throw ("docker {0} failed with exit {1}: {2}" -f ($Arguments -join ' '),$exitCode,$text) }
    return $text.Trim()
}
function Get-DockerLines {
    param([string[]]$Arguments)
    $text=Invoke-DockerText -Arguments $Arguments
    if ([string]::IsNullOrWhiteSpace($text)) { return @() }
    return @($text -split '\r?\n' | Where-Object { $_.Trim() })
}
function Get-DockerJson {
    param([string[]]$Arguments)
    return ((Invoke-DockerText -Arguments $Arguments) | ConvertFrom-Json)
}
function Get-Prop {
    param($Object,[string]$Name,$Default='')
    if ($null -eq $Object) { return $Default }
    $p=$Object.PSObject.Properties[$Name]
    if ($null -eq $p -or $null -eq $p.Value) { return $Default }
    return $p.Value
}
function Assert-Equal {
    param($Actual,$Expected,[string]$Label)
    if ([string]$Actual -cne [string]$Expected) {
        throw ("STOP: {0} mismatch: actual='{1}' expected='{2}'" -f $Label,$Actual,$Expected)
    }
}
function Assert-Set {
    param([string[]]$Actual,[string[]]$Expected,[string]$Label)
    $a=@($Actual | Sort-Object -CaseSensitive); $e=@($Expected | Sort-Object -CaseSensitive)
    if ($a.Count -ne $e.Count -or (Compare-Object -ReferenceObject $e -DifferenceObject $a -CaseSensitive)) {
        throw ("STOP: {0} mismatch: actual=[{1}] expected=[{2}]" -f $Label,($a -join ';'),($e -join ';'))
    }
}
function Get-EffectiveMounts {
    param($Info)
    $m=@($Info.Mounts | Where-Object { $_.Type -cne 'tmpfs' })
    return @($m | ForEach-Object { '{0}|{1}|{2}|rw={3}' -f $_.Type,$_.Name,$_.Destination,$_.RW })
}
function Get-RequestedMounts {
    param($Info)
    $m=@($Info.HostConfig.Mounts | Where-Object { $null -ne $_ })
    return @($m | ForEach-Object {
        $sub=[string](Get-Prop $_.VolumeOptions 'Subpath' '')
        $ro=$false
        $rp=$_.PSObject.Properties['ReadOnly']
        if ($null -ne $rp -and $rp.Value -eq $true) { $ro=$true }
        '{0}|{1}|{2}|ro={3}|subpath={4}' -f $_.Type,$_.Source,$_.Target,$ro,$sub
    })
}
function Assert-RepoMountContract {
    param($Info,[bool]$WithClient)
    $effective=@(
        'volume|fortis-repo-workspace|/workspace/files|rw=True'
        'volume|fortis-repo-workspace|/workspace/repo|rw=True'
        'volume|fortis-repo-workspace|/workspace/repos|rw=True'
    )
    $requested=@(
        'volume|fortis-repo-workspace|/workspace/repo|ro=False|subpath='
        'volume|fortis-repo-workspace|/workspace/repos|ro=False|subpath=Repos'
        'volume|fortis-repo-workspace|/workspace/files|ro=False|subpath=Workfiles'
    )
    if ($WithClient) {
        $effective+='volume|fortis-r7-client|/run/fortis-client|rw=False'
        $requested+='volume|fortis-r7-client|/run/fortis-client|ro=True|subpath='
    }
    Assert-Set (Get-EffectiveMounts $Info) $effective 'Repo effective mounts'
    Assert-Set (Get-RequestedMounts $Info) $requested 'Repo requested mounts'
}
function Assert-R7 {
    $ids=@(Get-DockerLines @('ps','-a','--no-trunc','--filter','label=com.docker.compose.project=fortis-r7-p1','--filter','label=com.docker.compose.service=fortis','--format','{{.ID}}'))
    if ($ids.Count -ne 1) { throw 'STOP: expected exactly one R7 service container' }
    Assert-Equal $ids[0] $script:ExpectedR7ContainerId 'R7 container ID unchanged'
    $r=Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$ids[0])
    if ($r.State.Running -ne $true) { throw 'STOP: R7 is not running' }
    Assert-Equal $r.State.Health.Status 'healthy' 'R7 health'
    Assert-Equal $r.Image $R7ImageId 'R7 image ID'
    Assert-Set (Get-EffectiveMounts $r) @(
        'volume|fortis-r7-state|/state|rw=True'
        'volume|fortis-r7-grant|/run/r7-grant|rw=False'
        'volume|fortis-r7-identity|/run/r7-identity|rw=False'
        'volume|fortis-r7-sonar-token|/run/r7-sonar-token|rw=False'
    ) 'R7 mounts'
    Assert-Set @($r.HostConfig.Binds) @(
        'fortis-r7-state:/state:rw'
        'fortis-r7-identity:/run/r7-identity:ro'
        'fortis-r7-grant:/run/r7-grant:ro'
        'fortis-r7-sonar-token:/run/r7-sonar-token:ro'
    ) 'R7 binds'
}
function Save-State {
    param($State)
    $State.Phase='RolledBack'
    $State.UpdatedAt=(Get-Date).ToString('o')
    $json=ConvertTo-Json -InputObject $State -Depth 8
    $tmp=$StatePath+'.tmp'
    $backup=$StatePath+'.previous'
    $encoding=New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
    [System.IO.File]::WriteAllText($tmp,$json,$encoding)
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
    [System.IO.File]::Replace($tmp,$StatePath,$backup)
}

if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
    throw 'STOP: GO-3 state file missing; cannot safely identify replacement'
}
$state=Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
Assert-Equal $state.Schema 1 'state schema'
Assert-Equal $state.CutoverId $CutoverId 'cutover ID'
Assert-Equal $state.OldName $OldName 'original container name'
Assert-Equal $state.RollbackName $RollbackName 'rollback container name'
Assert-Equal $state.OldContainerId $OldContainerId 'old full container ID'
Assert-Equal $state.ImageId $ImageId 'Repo image ID'
$script:ExpectedR7ContainerId=[string]$state.R7ContainerId
if ($script:ExpectedR7ContainerId -notmatch '^[0-9a-f]{64}$') { throw 'STOP: saved R7 full container ID missing or invalid' }

$oldOriginal=@(Get-DockerLines @('ps','-a','--no-trunc','--filter',("name=^/{0}$" -f $OldName),'--format','{{.ID}}'))
$oldRollback=@(Get-DockerLines @('ps','-a','--no-trunc','--filter',("name=^/{0}$" -f $RollbackName),'--format','{{.ID}}'))
if ($oldOriginal.Count -gt 1 -or $oldRollback.Count -gt 1) { throw 'STOP: duplicate container name results' }

$oldLocation=''
if ($oldRollback.Count -eq 1 -and $oldRollback[0] -ceq $OldContainerId) {
    $oldLocation='rollback'
    if ($oldOriginal.Count -ne 0) { throw 'STOP: original name is occupied' }
}
elseif ($oldOriginal.Count -eq 1 -and $oldOriginal[0] -ceq $OldContainerId) {
    $oldLocation='original'
    if ($oldRollback.Count -ne 0) { throw 'STOP: rollback name is unexpectedly occupied' }
}
else {
    throw 'STOP: original Repo full ID not found under either expected name'
}

$newId=[string]$state.NewContainerId
$newExists=$false
$newInfo=$null
if ($oldLocation -eq 'rollback') {
    $target=@(Get-DockerLines @('ps','-a','--no-trunc','--filter',("name=^/{0}$" -f $OldName),'--format','{{.ID}}'))
    if ($target.Count -gt 1) { throw 'STOP: multiple containers occupy original Repo name' }
    if ($target.Count -eq 1) {
        if ($newId -notmatch '^[0-9a-f]{64}$') {
            throw 'STOP: replacement exists but state file has no recorded full ID; manual review required'
        }
        Assert-Equal $target[0] $newId 'replacement full ID'
        $newInfo=Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$newId)
        Assert-Equal $newInfo.Id $newId 'replacement inspect ID'
        Assert-Equal $newInfo.Name ('/' + $OldName) 'replacement name'
        Assert-Equal $newInfo.Image $ImageId 'replacement image ID'
        Assert-RepoMountContract $newInfo $true
        $newExists=$true
    }
}

$confirmation=Read-Host 'Type ROLLBACK-GO3-REPO to restore the original Repo'
if ($confirmation -cne 'ROLLBACK-GO3-REPO') {
    Write-Output 'ROLLBACK_NOT_AUTHORIZED'
    return
}

if ($newExists) {
    if ($newInfo.State.Running -eq $true) {
        Invoke-DockerText @('stop','--time','30',$newId) | Out-Null
    }
    # No -v: this removes only the recorded replacement container.
    Invoke-DockerText @('rm',$newId) | Out-Null
    $remaining=@(Get-DockerLines @('ps','-a','--no-trunc','--filter',("name=^/{0}$" -f $OldName),'--format','{{.ID}}'))
    if ($remaining.Count -ne 0) { throw 'STOP: replacement name remains after container removal' }
}

if ($oldLocation -eq 'rollback') {
    Invoke-DockerText @('rename',$RollbackName,$OldName) | Out-Null
}
$restored=Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$OldName)
Assert-Equal $restored.Id $OldContainerId 'restored Repo full ID'
Assert-Equal $restored.Image $ImageId 'restored Repo image ID'
Assert-RepoMountContract $restored $false
if ($restored.State.Running -ne $true) {
    Invoke-DockerText @('start',$OldName) | Out-Null
}
$restored=Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$OldName)
if ($restored.State.Running -ne $true) { throw 'STOP: original Repo did not return to running state' }
Assert-Equal $restored.Id $OldContainerId 'running restored Repo ID'
Assert-RepoMountContract $restored $false

$clientAttachments=@(Get-DockerLines @('ps','-a','--no-trunc','--filter','volume=fortis-r7-client','--format','{{.ID}}'))
if ($clientAttachments.Count -ne 0) { throw 'STOP: client volume still attached after rollback' }
$egress=Get-DockerJson @('network','inspect','--format','{{json .}}','fortis-repo-egress')
$internal=Get-DockerJson @('network','inspect','--format','{{json .}}','rdg-personal_agents')
Assert-Equal $egress.Id $EgressNetworkId 'fortis-repo-egress ID'
Assert-Equal $internal.Id $InternalNetworkId 'rdg-personal_agents ID'
Assert-Equal $egress.Internal $false 'fortis-repo-egress Internal'
Assert-Equal $internal.Internal $true 'rdg-personal_agents Internal'
Assert-R7

Save-State $state
Write-Output 'GO3_ROLLBACK_RESTORED_ORIGINAL_REPO_PASS'
Write-Output ("RESTORED_REPO_CONTAINER_ID=" + $OldContainerId)
Write-Output 'CLIENT_VOLUME_UNATTACHED=YES'
Write-Output 'NO_VOLUME_REMOVAL'
Write-Output 'NO_R7_RESTART'
Write-Output 'NO_SONAR_REQUEST'
