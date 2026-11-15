#requires -Version 5.1
<#
Opt-in Script Catalog adapter for a separate GET-only Mirror HTTPS listener.
First use saves existing file PATHS, never starts the listener.
No Git update, credential creation, service install, Docker writes or firewall changes.
#>
[CmdletBinding()]
param([switch]$Status)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$pin = 'c0f5db887f29996ee3464cf991674c31d0a22eaf'
$configPath = Join-Path $env:LOCALAPPDATA 'Fortis\MirrorRead\script-center-paths.json'

function Assert-LocalFile {
    param([string]$Path, [string]$Label, [bool]$Private)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathRooted($Path)) {
        throw ('MIRROR_BAD_PATH_' + $Label)
    }
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('MIRROR_OUTSIDE_LOCALAPPDATA_' + $Label)
    }
    $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if (-not ($item -is [IO.FileInfo]) -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw ('MIRROR_UNSAFE_FILE_' + $Label)
    }
    if ($Private) {
        $acl = Get-Acl -LiteralPath $full
        $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $allowed = @($owner, 'S-1-5-18', 'S-1-5-32-544')
        if (-not $acl.AreAccessRulesProtected -or
            $allowed -cnotcontains $acl.GetOwner(
                [Security.Principal.SecurityIdentifier]).Value) {
            throw ('MIRROR_PRIVATE_ACL_' + $Label)
        }
        foreach ($rule in $acl.GetAccessRules($true, $true,
                [Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -eq 'Allow' -and
                $allowed -cnotcontains $rule.IdentityReference.Value) {
                throw ('MIRROR_PRIVATE_ACL_' + $Label)
            }
        }
    }
    return $full
}

function Get-MirrorLauncher {
    $root = Join-Path $env:USERPROFILE 'Repos\docke-windows-mirror'
    $launcher = Join-Path $root 'Start-MirrorGatewayReadOnly.ps1'
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        throw 'MIRROR_LAUNCHER_MISSING'
    }
    $git = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source
    $null = Get-Command py.exe -CommandType Application -ErrorAction Stop
    $null = Get-Command docker.exe -CommandType Application -ErrorAction Stop
    $branch = (& $git -C $root branch --show-current | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -cne 'main') { throw 'MIRROR_BRANCH_CHANGED' }
    $sha = (& $git -C $root rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sha -cne $pin) { throw 'MIRROR_SOURCE_PIN_CHANGED' }
    # The GET-only listener imports mirror_api.operations, which changed in
    # Mirror PR #19-22. Require the exact reviewed source objects, not a
    # movable branch or merely a matching launcher filename.
    $runtimeBlobs = [ordered]@{
        'Start-MirrorGatewayReadOnly.ps1' = '368db7bb6ac4b64d6ecff0fda76a62ae3ff5779c'
        'mirror_api/__init__.py' = '9cebe46e8c113d55d1aca036cca594a2f2853b56'
        'mirror_api/gateway_readonly.py' = '93c61da0270f1d5d6fe97ba36a34579c403332d9'
        'mirror_api/operations.py' = '43d752412c8102a92c0804aa254623ce230347a9'
    }
    foreach ($rel in $runtimeBlobs.Keys) {
        $actual = (& $git -C $root rev-parse --verify ('HEAD:' + $rel) | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $actual -cne $runtimeBlobs[$rel]) {
            throw 'MIRROR_RUNTIME_BLOB_MISMATCH'
        }
    }
    $dirty = @(& $git -C $root status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'MIRROR_SOURCE_DIRTY' }
    $origin = (& $git -C $root remote get-url origin | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $origin -notmatch
        '^(?i:https://github\.com/AlexPastukhh/docke-windows-mirror(?:\.git)?|git@github\.com:AlexPastukhh/docke-windows-mirror(?:\.git)?)$') {
        throw 'MIRROR_ORIGIN_DENIED'
    }
    return $launcher
}

function Get-OwnerSetup {
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing -ErrorAction Stop
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Fortis Mirror — HTTPS READ'
    $form.Size = New-Object System.Drawing.Size(780, 330)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $help = New-Object System.Windows.Forms.Label
    $help.Text = 'Укажите существующие файлы. Сохраняются только пути. API при настройке не запускается.'
    $help.Location = New-Object System.Drawing.Point(15, 15)
    $help.Size = New-Object System.Drawing.Size(740, 40)
    $form.Controls.Add($help)
    $fields = [ordered]@{ serverCert='Сертификат PEM'; serverKey='Ключ PEM'; secretFile='Bearer-файл' }
    $boxes = @{}
    $i = 0
    foreach ($name in $fields.Keys) {
        $top = 68 + $i * 60
        $label = New-Object System.Windows.Forms.Label
        $label.Text = $fields[$name]
        $label.Location = New-Object System.Drawing.Point(15, $top)
        $label.Size = New-Object System.Drawing.Size(140, 25)
        $form.Controls.Add($label)
        $box = New-Object System.Windows.Forms.TextBox
        $box.Location = New-Object System.Drawing.Point(158, $top)
        $box.Size = New-Object System.Drawing.Size(495, 26)
        $form.Controls.Add($box)
        $boxes[$name] = $box
        $browse = New-Object System.Windows.Forms.Button
        $browse.Text = 'Обзор...'
        $browse.Tag = $box
        $browse.Location = New-Object System.Drawing.Point(665, ($top - 2))
        $browse.Size = New-Object System.Drawing.Size(88, 29)
        $browse.Add_Click({
            param($sender, $eventArgs)
            $picker = New-Object System.Windows.Forms.OpenFileDialog
            $picker.CheckFileExists = $true
            try {
                if ($picker.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
                    $sender.Tag.Text = $picker.FileName
                }
            } finally { $picker.Dispose() }
        })
        $form.Controls.Add($browse)
        $i++
    }
    $save = New-Object System.Windows.Forms.Button
    $save.Text = 'Проверить и сохранить'
    $save.Location = New-Object System.Drawing.Point(485, 260)
    $save.Size = New-Object System.Drawing.Size(170, 32)
    $save.Add_Click({
        try {
            $selected = [ordered]@{}
            foreach ($name in $fields.Keys) {
                $selected[$name] = Assert-LocalFile $boxes[$name].Text $name ($name -ne 'serverCert')
            }
            $form.Tag = $selected
            $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
            $form.Close()
        } catch {
            [void][System.Windows.Forms.MessageBox]::Show($form, $_.Exception.Message,
                'Mirror HTTPS: STOP', [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Warning)
        }
    })
    $form.Controls.Add($save)
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Отмена'
    $cancel.Location = New-Object System.Drawing.Point(665, 260)
    $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.Add($cancel)
    $form.AcceptButton = $save
    $form.CancelButton = $cancel
    try {
        if ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            return $form.Tag
        }
        return $null
    } finally { $form.Dispose() }
}

function Save-OwnerSetup {
    param($Paths)
    $folder = Split-Path -Parent $configPath
    if (-not (Test-Path -LiteralPath $folder)) {
        $null = New-Item -ItemType Directory -Path $folder -Force
    }
    if (Test-Path -LiteralPath $configPath) { throw 'MIRROR_CONFIG_ALREADY_EXISTS' }
    $temp = Join-Path $folder ('setup-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temp, ($Paths | ConvertTo-Json -Depth 3),
                                [Text.UTF8Encoding]::new($false))
        $acl = Get-Acl -LiteralPath $temp
        $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetOwner($owner)
        $acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $owner, [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow))
        Set-Acl -LiteralPath $temp -AclObject $acl -ErrorAction Stop
        $null = Assert-LocalFile $temp 'newConfig' $true
        Move-Item -LiteralPath $temp -Destination $configPath -ErrorAction Stop
        $null = Assert-LocalFile $configPath 'config' $true
    } finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
        }
    }
}

try {
    if ($env:OS -cne 'Windows_NT') { throw 'MIRROR_WINDOWS_REQUIRED' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ([Security.Principal.WindowsPrincipal]::new($identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'MIRROR_ELEVATED_DENIED'
    }
    $launcher = Get-MirrorLauncher
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        if ($Status) {
            Write-Host 'MIRROR_HTTPS_CONFIG=NOT_CONFIGURED'
            return
        }
        $settings = Get-OwnerSetup
        if ($null -eq $settings) {
            Write-Host 'MIRROR_HTTPS_SETUP_CANCELLED'
            return
        }
        Save-OwnerSetup $settings
        Write-Host 'MIRROR_HTTPS_PATHS_CONFIGURED=YES'
        Write-Host 'MIRROR_HTTPS_STARTED=NO'
        return
    }
    $config = Assert-LocalFile $configPath 'config' $true
    $settings = Get-Content -LiteralPath $config -Raw -Encoding UTF8 |
        ConvertFrom-Json -ErrorAction Stop
    $names = @('serverCert','serverKey','secretFile')
    if (@($settings.PSObject.Properties.Name).Count -ne $names.Count -or
        @($names | Where-Object { $settings.PSObject.Properties.Name -cnotcontains $_ }).Count) {
        throw 'MIRROR_CONFIG_SCHEMA_INVALID'
    }
    $cert = Assert-LocalFile $settings.serverCert 'serverCert' $false
    $key = Assert-LocalFile $settings.serverKey 'serverKey' $true
    $secret = Assert-LocalFile $settings.secretFile 'secretFile' $true
    if (@(Get-NetTCPConnection -LocalPort 443 -State Listen -ErrorAction SilentlyContinue).Count) {
        Write-Host 'MIRROR_HTTPS_PORT_443=OCCUPIED_OR_UNKNOWN'
        return
    }
    if ($Status) {
        Write-Host 'MIRROR_HTTPS_SOURCE_AND_PATHS=PASS'
        Write-Host 'MIRROR_HTTPS_PORT_443=AVAILABLE'
        Write-Host 'MIRROR_HTTPS_LIVE_HEALTH=NOT_TESTED'
        return
    }
    Write-Host 'MIRROR_SCRIPT_CENTER_PREFLIGHT_PASS'
    Write-Host 'Starting standalone Mirror GET-only listener on 127.0.0.1:443; Ctrl+C stops it.'
    & $launcher -SourceSha $pin -Bind '127.0.0.1' -CertificatePath $cert -KeyPath $key -SecretFile $secret -AllowedRepo @('fortis-approvals')
    if (-not $?) { throw 'MIRROR_READ_LISTENER_FAILED' }
} catch {
    [Console]::Error.WriteLine('MIRROR_SCRIPT_CENTER_STOP: ' + $_.Exception.Message)
    exit 1
}
