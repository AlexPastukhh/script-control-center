# GO-3 release verification: pin the approved image and verify both Windows Guards.
# Read-only only. No tunnel/container lifecycle, profile edits, runtime repair or Sonar calls.
$ErrorActionPreference = 'Stop'
$expectedContainerId = '04fed7b726a8536d63c4d2533cfc7ee3072956ff0c3f571504d7f709f7909030'
$expectedImageRef = 'fortis-repo-mode:dc-0.2.52'
# Owner-provided image ID; it matches the retained BuildKit manifest-list digest.
$expectedImageId = 'sha256:2c84fabebb34265e53b020f21a11683a4c68cbb41a08f5f11f9b01c7de5ed8c9'
$repoRoot = Join-Path $env:USERPROFILE 'Repos\tunnel\runtime\windows-repo\current'
if ($env:OS -ne 'Windows_NT') { throw 'STOP: run this script in Windows PowerShell.' }
if (-not (Test-Path -LiteralPath $repoRoot -PathType Container)) { throw "STOP: tunnel source not found: $repoRoot" }

$manifestPath = Join-Path $repoRoot 'source-manifest.json'
$expectedManifestHash = '8036348F69BB37F94F5E6FDE529BF44A292F4EB91977CE91A491E9860AADBFCA'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'STOP: source manifest missing' }
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
if ($manifestHash -cne $expectedManifestHash) { throw 'STOP: tunnel source manifest fingerprint mismatch' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schema_version -ne 1 -or
    $manifest.status -cne 'TUNNEL_CANONICAL_SHARED_GUARD_POLICY_GO3C' -or
    $manifest.source_repository -cne 'tunnel' -or
    $manifest.source_ref -cne 'tunnel:main' -or
    $manifest.source_revision -cne 'AEF7D17084F951AEAB6F98CF91E7A39E1D6043A0DAC6BC123DBF99463887B0F9' -or
    @($manifest.files).Count -ne 9) {
    throw 'STOP: tunnel source manifest metadata mismatch'
}
$expectedFiles = @(
    'Repo1-Worker.ps1'
    'Repo2-Worker.ps1'
    'launchers/chatgpt-repo-mode/control.ps1'
    'launchers/chatgpt-repo-mode/run-tunnel-quiet-logs.ps1'
    'launchers/FortisRDGLauncherV2/Common.ps1'
    'launchers/FortisRDGLauncherV2/Start-Fortis.ps1'
    'launchers/FortisRepoPlus2/Repo2DirectGuard.ps1'
    'launchers/FortisRepoPlus2/FortisRepo2-quiet-logs.ps1'
    'launchers/shared/FortisContainerPolicy.ps1'
)
$actualFiles = @($manifest.files | ForEach-Object { [string]$_.path } | Sort-Object)
$sortedExpectedFiles = @($expectedFiles | Sort-Object)
if (Compare-Object $sortedExpectedFiles $actualFiles -CaseSensitive) {
    throw 'STOP: manifest file inventory mismatch'
}
foreach ($relativePath in $expectedFiles) {
    $entry = @($manifest.files | Where-Object { $_.path -ceq $relativePath })
    if ($entry.Count -ne 1 -or [string]$entry[0].sha256 -cnotmatch '^[0-9A-F]{64}$') {
        throw "STOP: invalid manifest entry: $relativePath"
    }
    $filePath = Join-Path $repoRoot ($relativePath.Replace('/','\'))
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash -cne $entry[0].sha256) {
        throw "STOP: pinned tunnel source hash mismatch: $relativePath"
    }
}
Write-Output 'GO3_TUNNEL_SOURCE_MANIFEST_PASS'

function ConvertFrom-GO3InspectObject {
    param([string[]]$Lines, [string]$Kind)
    $json = @($Lines) -join "`n"
    if ([string]::IsNullOrWhiteSpace($json)) { throw "STOP: Docker $Kind inspect returned no JSON." }
    try { $value = ConvertFrom-Json -InputObject $json -ErrorAction Stop }
    catch { throw "STOP: Docker $Kind inspect returned invalid JSON." }
    if ($null -eq $value -or $value -is [System.Array] -or
        $value -is [string] -or $value -is [System.ValueType]) {
        throw "STOP: Docker $Kind inspect did not return one JSON object."
    }
    return $value
}

function Get-GO3PropertyValue {
    param($Object, [string]$Name, $Default = $null)
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

$docker = (Get-Command docker.exe -CommandType Application -ErrorAction Stop).Source
$containerRaw = @(& $docker inspect --type container --format '{{json .}}' -- $expectedContainerId 2>$null)
if ($LASTEXITCODE -ne 0 -or $containerRaw.Count -eq 0) { throw 'STOP: approved Repo container inspect failed.' }
$container = ConvertFrom-GO3InspectObject -Lines $containerRaw -Kind 'container'
$containerCmd = @($container.Config.Cmd | ForEach-Object { [string]$_ })
if ($container.Id -cne $expectedContainerId -or $container.Name -cne '/fortis-repo-test' -or
    $container.State.Running -ne $true -or $container.Config.Image -cne $expectedImageRef -or
    $container.Image -cne $expectedImageId -or $container.Config.User -cne '1000:1000' -or
    $container.Config.WorkingDir -cne '/workspace/repos' -or $containerCmd.Count -ne 2 -or
    $containerCmd[0] -cne 'sleep' -or $containerCmd[1] -cne 'infinity') {
    throw 'STOP: running Repo container identity, image or runtime defaults differ from the approved baseline.'
}

$imageRaw = @(& $docker image inspect --format '{{json .}}' $expectedImageId 2>$null)
if ($LASTEXITCODE -ne 0 -or $imageRaw.Count -eq 0) { throw 'STOP: approved Repo image inspect failed.' }
$image = ConvertFrom-GO3InspectObject -Lines $imageRaw -Kind 'image'
$imageTags = @($image.RepoTags | ForEach-Object { [string]$_ })
$imageCmd = @($image.Config.Cmd | ForEach-Object { [string]$_ })
$imageEnv = @($image.Config.Env | ForEach-Object { [string]$_ })
$requiredImageEnv = @('MCP_CLIENT_DOCKER=true','HOME=/home/node','NODE_ENV=production')
if ($image.Id -cne $expectedImageId -or $imageTags -cnotcontains $expectedImageRef -or
    $image.Config.User -cne '1000:1000' -or $image.Config.WorkingDir -cne '/workspace/repo' -or
    $imageCmd.Count -ne 2 -or $imageCmd[0] -cne 'sleep' -or $imageCmd[1] -cne 'infinity' -or
    @($requiredImageEnv | Where-Object { $imageEnv -cnotcontains $_ }).Count -ne 0) {
    throw 'STOP: approved Repo image ID, tag or reviewed build defaults differ.'
}
Write-Output 'GO3_IMAGE_IDENTITY_PASS'

# Emit a sanitized, non-secret snapshot for the fields not yet Guard-pinned.
$hostConfig = $container.HostConfig
$tmpfsEntries = @()
$tmpfsMap = Get-GO3PropertyValue $hostConfig 'Tmpfs'
if ($null -ne $tmpfsMap) {
    foreach ($property in $tmpfsMap.PSObject.Properties) {
        $tmpfsEntries += [pscustomobject]@{ Path=[string]$property.Name; Options=[string]$property.Value }
    }
}
$mountEntries = @()
foreach ($mount in @((Get-GO3PropertyValue $container 'Mounts'))) {
    # Docker container inspect Mounts uses Name/Destination/RW, not Source/Target/ReadOnly.
    # Do not emit the Docker host-side Source path; it is not needed for the volume map.
    $mountType = [string](Get-GO3PropertyValue $mount 'Type' '')
    $mountName = [string](Get-GO3PropertyValue $mount 'Name' '')
    $mountDestination = [string](Get-GO3PropertyValue $mount 'Destination' '')
    $mountRW = Get-GO3PropertyValue $mount 'RW' $null
    if ([string]::IsNullOrWhiteSpace($mountType) -or
        [string]::IsNullOrWhiteSpace($mountDestination) -or $null -eq $mountRW) {
        throw 'STOP: Docker mount inspection is missing Type, Destination or RW.'
    }
    if ($mountType -ceq 'volume' -and [string]::IsNullOrWhiteSpace($mountName)) {
        throw 'STOP: Docker volume mount inspection is missing the volume Name.'
    }
    $mountEntries += [pscustomobject]@{
        Type=$mountType; VolumeName=$mountName
        Destination=$mountDestination; ReadWrite=[bool]$mountRW
        Mode=[string](Get-GO3PropertyValue $mount 'Mode' '')
        Driver=[string](Get-GO3PropertyValue $mount 'Driver' '')
        Propagation=[string](Get-GO3PropertyValue $mount 'Propagation' '')
    }
}
$networkMap = Get-GO3PropertyValue $container.NetworkSettings 'Networks'
$networkNames = @()
if ($null -ne $networkMap) { $networkNames = @($networkMap.PSObject.Properties | ForEach-Object { $_.Name }) }
$portBindings = Get-GO3PropertyValue $hostConfig 'PortBindings'
$portBindingCount = 0
if ($null -ne $portBindings) { $portBindingCount = @($portBindings.PSObject.Properties).Count }
$restartPolicy = Get-GO3PropertyValue $hostConfig 'RestartPolicy'
$auxiliary = [pscustomobject]@{
    MemoryBytes=(Get-GO3PropertyValue $hostConfig 'Memory')
    MemorySwapBytes=(Get-GO3PropertyValue $hostConfig 'MemorySwap')
    NanoCpus=(Get-GO3PropertyValue $hostConfig 'NanoCpus')
    PidsLimit=(Get-GO3PropertyValue $hostConfig 'PidsLimit')
    ShmSizeBytes=(Get-GO3PropertyValue $hostConfig 'ShmSize')
    RestartPolicyName=(Get-GO3PropertyValue $restartPolicy 'Name')
    Tmpfs=$tmpfsEntries; Networks=$networkNames
    PublishedPortBindingCount=$portBindingCount; Mounts=$mountEntries
}
Write-Output ('GO3_CONTAINER_AUXILIARY_CONFIG=' + ($auxiliary | ConvertTo-Json -Depth 5 -Compress))

$repo1Path = Join-Path $repoRoot 'launchers\chatgpt-repo-mode\run-tunnel-quiet-logs.ps1'
if (-not (Test-Path -LiteralPath $repo1Path -PathType Leaf)) { throw 'STOP: Repo1 Guard launcher missing' }
$repo1Output = @(& $repo1Path -Mode Preview)
$repo1Json = (($repo1Output | ForEach-Object { [string]$_ }) -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($repo1Json)) { throw 'STOP: Repo1 Preview returned no result' }
$repo1 = $repo1Json | ConvertFrom-Json -ErrorAction Stop
if ($repo1.Mode -cne 'Preview' -or $repo1.Preflight -cne 'PASS' -or
    $repo1.ContainerId -cne $expectedContainerId -or
    $repo1.ImageId -cne $expectedImageId -or
    [string]$repo1.LinuxUid -cne '1000' -or
    $repo1.LinuxSecurityVerified -ne $true -or
    $repo1.DirectEntryAvailable -ne $true -or
    $repo1.QuietPreloadProbe -cne 'PASS') {
    throw 'STOP: Repo1 Guard Preview did not fully pass'
}
Write-Output 'GO3_REPO1_GUARD_PREVIEW_PASS'
Write-Output ("REPO1_EXISTING_TUNNEL_COUNT=" + $repo1.ExistingTunnelCount)
Write-Output ("REPO1_EXISTING_PROFILE_COUNT=" + $repo1.ExistingProfileCount)

$repo2Path = Join-Path $repoRoot 'launchers\FortisRepoPlus2\Repo2DirectGuard.ps1'
if (-not (Test-Path -LiteralPath $repo2Path -PathType Leaf)) { throw 'STOP: Repo2 Direct Guard missing' }
$repo2Output = @(& $repo2Path -Mode Preview -ContainerId $expectedContainerId)
$repo2Json = (($repo2Output | ForEach-Object { [string]$_ }) -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($repo2Json)) { throw 'STOP: Repo2 Preview returned no result' }
$repo2 = $repo2Json | ConvertFrom-Json -ErrorAction Stop
if ($repo2.Mode -cne 'Preview' -or $repo2.Preflight -cne 'PASS' -or
    $repo2.ContainerId -cne $expectedContainerId -or
    $repo2.ImageId -cne $expectedImageId -or
    $repo2.ContainerIdMatched -ne $true -or
    [string]$repo2.LinuxUid -cne '1000' -or
    $repo2.LinuxSecurityVerified -ne $true -or
    $repo2.DirectEntryAvailable -ne $true -or
    $repo2.NoWindowsProfileEdits -ne $true) {
    throw 'STOP: Repo2 Guard Preview did not fully pass'
}
Write-Output 'GO3_REPO2_GUARD_PREVIEW_PASS'
Write-Output 'GO3_BOTH_GUARD_PREVIEWS_PASS'
Write-Output 'GO3_RELEASE_VERIFICATION_PASS'
Write-Output 'NO_TUNNEL_START_OR_STOP'
Write-Output 'NO_WINDOWS_PROFILE_EDITS'
Write-Output 'NO_RUNTIME_CONFIG_REPAIR'
Write-Output 'NO_SONAR_REQUEST'
Write-Output 'REPO_RECONNECT_PENDING'
