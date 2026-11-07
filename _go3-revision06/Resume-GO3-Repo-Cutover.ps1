#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

# Resume the already-authorized GO-3 operation from ReplacementCreated.
# Does not recreate Repo, delete volumes, restart R7, or contact SonarCloud.
$CutoverId = '20261102-144258'
$OldName = 'fortis-repo-test'
$RollbackName = 'fortis-repo-test-go3-rollback-20261102-144258'
$OldContainerId = '0c0f8cb2d608679b4c3de51c527cbe2aca687cbf1360e6bf116444b2a0928cc2'
$ImageId = 'sha256:2c84fabebb34265e53b020f21a11683a4c68cbb41a08f5f11f9b01c7de5ed8c9'
$R7ImageId = 'sha256:23667619c6dfac33466d0e0b4b60bcb22fb7dccb14b0a024f2c062bac0a18ce4'
$EgressNetworkId = '65a8a1d816e3c6e5703d976d31514ecd657dfb14f5e5fafaba0e5208046e3ffd'
$InternalNetworkId = 'a4b5b7e37facafe180b5bb572ea3d228e3349b77f6babb760452708082566524'
$StatePath = Join-Path $env:LOCALAPPDATA 'Fortis\GO3-Cutover\20261102-144258\go3-cutover-state.json'

if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
    throw 'STOP: Windows PowerShell 5.1 required'
}
$dockerCommand = Get-Command docker.exe -CommandType Application | Select-Object -First 1
if ($null -eq $dockerCommand) { throw 'STOP: docker.exe not found' }
$script:DockerExe = $dockerCommand.Source

function Invoke-DockerText {
    param([string[]]$Arguments)
    $lines = @(& $script:DockerExe @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($lines | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($exitCode -ne 0) { throw ("docker {0} failed ({1}): {2}" -f ($Arguments -join ' '),$exitCode,$text) }
    return $text.Trim()
}
function Get-DockerLines {
    param([string[]]$Arguments)
    $text = Invoke-DockerText -Arguments $Arguments
    if ([string]::IsNullOrWhiteSpace($text)) { return @() }
    return @($text -split '\r?\n' | Where-Object { $_.Trim() })
}
function Get-DockerJson {
    param([string[]]$Arguments)
    return (Invoke-DockerText -Arguments $Arguments | ConvertFrom-Json)
}
function Assert-Equal {
    param($Actual,$Expected,[string]$Label)
    if ([string]$Actual -cne [string]$Expected) {
        throw ("STOP: {0} mismatch: actual='{1}' expected='{2}'" -f $Label,$Actual,$Expected)
    }
}
function Assert-Set {
    param([string[]]$Actual,[string[]]$Expected,[string]$Label)
    $a = @($Actual | Sort-Object -CaseSensitive)
    $e = @($Expected | Sort-Object -CaseSensitive)
    if ($a.Count -ne $e.Count -or (Compare-Object $e $a -CaseSensitive)) {
        throw ("STOP: {0} mismatch: actual=[{1}] expected=[{2}]" -f $Label,($a -join ';'),($e -join ';'))
    }
}
function Get-EffectiveMounts {
    param($Info)
    $m = @($Info.Mounts | Where-Object { $_.Type -cne 'tmpfs' })
    return @($m | ForEach-Object { '{0}|{1}|{2}|rw={3}' -f $_.Type,$_.Name,$_.Destination,$_.RW })
}
function Get-RequestedMounts {
    param($Info)
    $m = @($Info.HostConfig.Mounts | Where-Object { $null -ne $_ })
    return @($m | ForEach-Object {
        $sub = ''
        if ($null -ne $_.VolumeOptions) { $sub = [string]$_.VolumeOptions.Subpath }
        $ro = $false
        if ($null -ne $_.PSObject.Properties['ReadOnly'] -and $_.ReadOnly -eq $true) { $ro = $true }
        '{0}|{1}|{2}|ro={3}|subpath={4}' -f $_.Type,$_.Source,$_.Target,$ro,$sub
    })
}
function Get-Prop {
    param($Object,[string]$Name,$Default='')
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}
function Get-Count {
    param($Value)
    if ($null -eq $Value) { return 0 }
    $items = @($Value | Where-Object { $null -ne $_ })
    return $items.Count
}
function Assert-RepoPolicy {
    param($Info)
    Assert-Equal $Info.Image $ImageId 'Repo image ID'
    Assert-Equal $Info.Config.User '1000:1000' 'Repo UID'
    Assert-Equal $Info.Config.WorkingDir '/workspace/repos' 'Repo workdir'
    if ($Info.HostConfig.ReadonlyRootfs -ne $true) { throw 'STOP: Repo rootfs is not read-only' }
    if ($Info.HostConfig.Privileged -ne $false) { throw 'STOP: Repo privileged flag enabled' }
    Assert-Set @($Info.HostConfig.CapDrop) @('ALL') 'Repo CapDrop'
    if ((Get-Count $Info.HostConfig.CapAdd) -ne 0) { throw 'STOP: Repo CapAdd is non-empty' }
    if ((Get-Count $Info.HostConfig.Binds) -ne 0) { throw 'STOP: unexpected Repo bind mounts' }
    if (@($Info.HostConfig.SecurityOpt).Count -ne 1 -or $Info.HostConfig.SecurityOpt[0] -notmatch '^no-new-privileges(:true)?$') {
        throw 'STOP: Repo no-new-privileges policy mismatch'
    }
    Assert-Equal $Info.HostConfig.NetworkMode 'fortis-repo-egress' 'Repo primary network'
    Assert-Equal $Info.HostConfig.RestartPolicy.Name 'unless-stopped' 'Repo restart policy'
    Assert-Equal $Info.HostConfig.Memory 4294967296 'Repo memory'
    Assert-Equal $Info.HostConfig.MemorySwap 5368709120 'Repo memory+swap'
    Assert-Equal $Info.HostConfig.NanoCpus 2000000000 'Repo CPU'
    Assert-Equal $Info.HostConfig.PidsLimit 512 'Repo PIDs limit'
    if ($Info.HostConfig.OomKillDisable -eq $true) { throw 'STOP: Repo OOM kill disable must not be enabled' }
    Assert-Equal $Info.HostConfig.ShmSize 67108864 'Repo shm size'
    Assert-Equal $Info.HostConfig.IpcMode 'private' 'Repo IPC mode'
    Assert-Equal $Info.HostConfig.CgroupnsMode 'private' 'Repo cgroup namespace'
    Assert-Equal ([string]$Info.HostConfig.Runtime) 'runc' 'Repo runtime'
    Assert-Equal $Info.HostConfig.LogConfig.Type 'json-file' 'Repo log driver'
    Assert-Equal ([string](Get-Prop $Info.HostConfig.Tmpfs '/tmp' '')) 'rw,nosuid,nodev,noexec,mode=1777,size=128m' 'Repo /tmp'
    Assert-Equal ([string](Get-Prop $Info.HostConfig.Tmpfs '/home/node' '')) 'rw,nosuid,nodev,noexec,uid=1000,gid=1000,mode=700,size=128m' 'Repo /home/node'
    $expectedEffective = @(
        'volume|fortis-repo-workspace|/workspace/files|rw=True'
        'volume|fortis-repo-workspace|/workspace/repo|rw=True'
        'volume|fortis-repo-workspace|/workspace/repos|rw=True'
        'volume|fortis-r7-client|/run/fortis-client|rw=False'
    )
    $expectedRequested = @(
        'volume|fortis-repo-workspace|/workspace/repo|ro=False|subpath='
        'volume|fortis-repo-workspace|/workspace/repos|ro=False|subpath=Repos'
        'volume|fortis-repo-workspace|/workspace/files|ro=False|subpath=Workfiles'
        'volume|fortis-r7-client|/run/fortis-client|ro=True|subpath='
    )
    Assert-Set (Get-EffectiveMounts $Info) $expectedEffective 'Repo effective mounts'
    Assert-Set (Get-RequestedMounts $Info) $expectedRequested 'Repo requested mounts'
    Assert-Set @($Info.NetworkSettings.Networks.PSObject.Properties.Name) @('fortis-repo-egress','rdg-personal_agents') 'Repo network attachments'
}
function Assert-R7 {
    param([string]$ExpectedId)
    $ids = @(Get-DockerLines @('ps','-a','--no-trunc','--filter','label=com.docker.compose.project=fortis-r7-p1','--filter','label=com.docker.compose.service=fortis','--format','{{.ID}}'))
    if ($ids.Count -ne 1) { throw 'STOP: expected exactly one R7 container' }
    Assert-Equal $ids[0] $ExpectedId 'R7 container ID'
    $r = Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$ExpectedId)
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

if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
    throw 'STOP: GO-3 state file not found; cannot resume safely'
}
$state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
Assert-Equal $state.Schema 1 'state schema'
Assert-Equal $state.CutoverId $CutoverId 'cutover ID'
Assert-Equal $state.OldName $OldName 'original Repo name'
Assert-Equal $state.RollbackName $RollbackName 'rollback Repo name'
Assert-Equal $state.OldContainerId $OldContainerId 'original Repo ID'
Assert-Equal $state.ImageId $ImageId 'Repo image ID'
if ($state.Phase -notin @('ReplacementCreated','ReplacementCreatedAndVerified','ReplacementRunningVerified')) {
    throw ("STOP: unexpected GO-3 phase: " + $state.Phase)
}
$newId = [string]$state.NewContainerId
$r7Id = [string]$state.R7ContainerId
if ($newId -notmatch '^[0-9a-f]{64}$' -or $r7Id -notmatch '^[0-9a-f]{64}$') {
    throw 'STOP: recorded Repo/R7 full container ID missing'
}

$old = Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$RollbackName)
Assert-Equal $old.Id $OldContainerId 'preserved old Repo ID'
if ($old.State.Running -ne $false) { throw 'STOP: preserved old Repo must remain stopped' }
$new = Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$newId)
Assert-Equal $new.Id $newId 'replacement full ID'
Assert-Equal $new.Name ('/' + $OldName) 'replacement name'
if ($new.State.Status -eq 'removing' -or $new.State.Status -eq 'dead') { throw 'STOP: replacement is not recoverable' }

$egress = Get-DockerJson @('network','inspect','--format','{{json .}}','fortis-repo-egress')
$internal = Get-DockerJson @('network','inspect','--format','{{json .}}','rdg-personal_agents')
Assert-Equal $egress.Id $EgressNetworkId 'egress network ID'
Assert-Equal $egress.Internal $false 'egress network policy'
Assert-Equal $internal.Id $InternalNetworkId 'internal network ID'
Assert-Equal $internal.Internal $true 'internal network policy'
$networkNames = @($new.NetworkSettings.Networks.PSObject.Properties.Name)
if ($networkNames -notcontains 'fortis-repo-egress') { throw 'STOP: replacement lost primary egress network' }
if ($networkNames -notcontains 'rdg-personal_agents') {
    Invoke-DockerText @('network','connect','rdg-personal_agents',$newId) | Out-Null
}
$new = Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$newId)
Assert-RepoPolicy $new

$clientVolume = Get-DockerJson @('volume','inspect','--format','{{json .}}','fortis-r7-client')
Assert-Equal $clientVolume.Name 'fortis-r7-client' 'client volume name'
Assert-Equal $clientVolume.Driver 'local' 'client volume driver'
Assert-Equal $clientVolume.Scope 'local' 'client volume scope'
Assert-Equal $clientVolume.Labels.'com.fortis.r7.p1.role' 'client-only' 'client volume role'
if ([string]$clientVolume.Labels.'com.fortis.r7.p1.provision-id' -notmatch '^[0-9a-fA-F]{32}$') {
    throw 'STOP: client volume provision ID invalid'
}
$attachments = @(Get-DockerLines @('ps','-a','--no-trunc','--filter','volume=fortis-r7-client','--format','{{.ID}}'))
Assert-Set $attachments @($newId) 'client volume attachment'
Assert-R7 $r7Id

if ($new.State.Running -ne $true) {
    Invoke-DockerText @('start',$newId) | Out-Null
}
$running = Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$newId)
if ($running.State.Running -ne $true) { throw 'STOP: replacement Repo did not start' }
Assert-RepoPolicy $running
$old = Get-DockerJson @('inspect','--type','container','--format','{{json .}}',$RollbackName)
Assert-Equal $old.Id $OldContainerId 'retained rollback Repo ID'
if ($old.State.Running -ne $false) { throw 'STOP: rollback Repo unexpectedly started' }
$attachments = @(Get-DockerLines @('ps','-a','--no-trunc','--filter','volume=fortis-r7-client','--format','{{.ID}}'))
Assert-Set $attachments @($newId) 'client volume final attachment'
Assert-R7 $r7Id

$state.Phase = 'ReplacementRunningVerified'
$state.UpdatedAt = (Get-Date).ToString('o')
$encoding = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
$json = ConvertTo-Json -InputObject $state -Depth 8
[System.IO.File]::WriteAllText($StatePath,$json,$encoding)

Write-Output 'GO3_REPO_RECREATE_AND_CLIENT_RO_MOUNT_PASS'
Write-Output ("NEW_REPO_CONTAINER_ID=" + $newId)
Write-Output ("ROLLBACK_REPO_CONTAINER_ID=" + $OldContainerId)
Write-Output 'OLD_REPO_PRESERVED_STOPPED=YES'
Write-Output 'R7_UNCHANGED_HEALTHY=YES'
Write-Output 'GO3_RESUME_PHASE=ReplacementRunningVerified'
Write-Output 'GUARD_PREVIEWS_AND_REPO_RECONNECT_PENDING'
Write-Output 'SONAR_REQUESTS=NONE'
Write-Output 'SONAR_POST=0'