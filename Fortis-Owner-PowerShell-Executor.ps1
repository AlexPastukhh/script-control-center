#requires -Version 5.1
<#
Script Catalog adapter for the existing opt-in Owner PowerShell Executor.
Does not provision credentials, create a receipt DB, or deploy Gateway.
Import as window (Своё окно) only; never run as Administrator.
#>
[CmdletBinding()]
param([switch]$Configure)
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
    if ($Label -ceq 'receiptDb') {
        # The executor may create a DB automatically; reject empty/wrong files.
        # This must be the owner's EXISTING SQLite receipt store, not a new one.
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open,
                                  [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            if ($stream.Length -lt 100) { throw 'OWNER_SHELL_RECEIPT_DB_INVALID' }
            $header = New-Object byte[] 16
            if ($stream.Read($header, 0, 16) -ne 16 -or
                [Text.Encoding]::ASCII.GetString($header) -cne ("SQLite format 3" + [char]0)) {
                throw 'OWNER_SHELL_RECEIPT_DB_INVALID'
            }
        } finally { $stream.Dispose() }
    }
    return $path
}

function Show-OwnerShellSetup {
    param($Previous)
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing -ErrorAction Stop

    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Fortis — настройка Windows Executor'
    $form.Size = New-Object System.Drawing.Size(850, 510)
    $form.MinimumSize = $form.Size
    $form.MaximumSize = $form.Size
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false

    $heading = New-Object System.Windows.Forms.Label
    $heading.Location = New-Object System.Drawing.Point(22, 18)
    $heading.Size = New-Object System.Drawing.Size(790, 48)
    $heading.Text = 'Выберите уже существующие файлы. Сохраняются только пути, не ключи или секреты.' +
        [Environment]::NewLine + 'Для receiptDb используйте ТУ ЖЕ базу квитанций, не новую пустую базу.'
    $form.Controls.Add($heading)

    $fields = [ordered]@{
        serverCert = @('Сертификат сервера', 'windows\server.pem')
        serverKey = @('Ключ сервера', 'windows\server.key')
        clientCA = @('CA клиента Gateway', 'windows\client-ca.pem')
        secretFile = @('Файл секрета', 'windows\secret.txt')
        receiptDb = @('База квитанций', 'receipts\owner-shell.sqlite3')
    }
    $boxes = @{}
    $index = 0
    foreach ($name in $fields.Keys) {
        $row = $fields[$name]
        $top = 83 + (54 * $index)
        $label = New-Object System.Windows.Forms.Label
        $label.Text = $row[0]
        $label.Location = New-Object System.Drawing.Point(20, $top)
        $label.Size = New-Object System.Drawing.Size(165, 24)
        $form.Controls.Add($label)

        $box = New-Object System.Windows.Forms.TextBox
        $box.Location = New-Object System.Drawing.Point(188, ($top - 3))
        $box.Size = New-Object System.Drawing.Size(530, 26)
        $box.Name = $name
        $suggestion = Join-Path (Join-Path $env:LOCALAPPDATA 'Fortis\OwnerShell') $row[1]
        if ($null -ne $Previous) {
            $box.Text = [string]$Previous.$name
        } elseif (Test-Path -LiteralPath $suggestion -PathType Leaf) {
            $box.Text = $suggestion
        }
        $boxes[$name] = $box
        $form.Controls.Add($box)

        $browse = New-Object System.Windows.Forms.Button
        $browse.Text = 'Обзор...'
        $browse.Location = New-Object System.Drawing.Point(728, ($top - 5))
        $browse.Size = New-Object System.Drawing.Size(85, 30)
        $browse.Tag = $box
        $browse.Add_Click({
            param($sender, $eventArgs)
            $picker = New-Object System.Windows.Forms.OpenFileDialog
            $picker.Title = 'Выберите существующий файл'
            $picker.CheckFileExists = $true
            $picker.Multiselect = $false
            $picker.RestoreDirectory = $true
            try {
                $destination = [System.Windows.Forms.TextBox]$sender.Tag
                if (Test-Path -LiteralPath $destination.Text -PathType Leaf) {
                    $picker.FileName = $destination.Text
                }
                if ($picker.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
                    $destination.Text = $picker.FileName
                }
            } finally { $picker.Dispose() }
        })
        $form.Controls.Add($browse)
        $index++
    }

    $notice = New-Object System.Windows.Forms.Label
    $notice.Location = New-Object System.Drawing.Point(20, 365)
    $notice.Size = New-Object System.Drawing.Size(785, 32)
    $notice.Text = 'Проверяются LOCALAPPDATA и права на приватные файлы. ' +
        'Ошибка появится здесь, окно останется открытым.'
    $form.Controls.Add($notice)

    $save = New-Object System.Windows.Forms.Button
    $save.Text = 'Проверить и сохранить'
    $save.Location = New-Object System.Drawing.Point(580, 402)
    $save.Size = New-Object System.Drawing.Size(150, 30)
    $save.Add_Click({
        try {
            $selected = [ordered]@{}
            foreach ($name in $fields.Keys) {
                $selected[$name] = Assert-PrivateFile ([string]$boxes[$name].Text) $name (
                    $name -in @('serverKey','secretFile','receiptDb'))
            }
            if ($null -ne $Previous -and
                $selected['receiptDb'] -cne $Previous.receiptDb) {
                throw 'OWNER_SHELL_RECEIPT_DB_CHANGE_REQUIRES_REVIEW'
            }
            $form.Tag = $selected
            $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
            $form.Close()
        } catch {
            $message = $_.Exception.Message
            if ($message -like 'OWNER_SHELL_ACL_*') {
                $message += [Environment]::NewLine +
                    'Права файла должны быть защищены: только текущий пользователь, SYSTEM и Administrators. ' +
                    'Настройка не меняет ACL автоматически.'
            }
            [void][System.Windows.Forms.MessageBox]::Show(
                $form, $message, 'Проверка файлов Executor',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Warning)
        }
    })
    $form.Controls.Add($save)

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Отмена'
    $cancel.Location = New-Object System.Drawing.Point(735, 402)
    $cancel.Size = New-Object System.Drawing.Size(78, 30)
    $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.Add($cancel)
    $form.AcceptButton = $save
    $form.CancelButton = $cancel

    try {
        $outcome = $form.ShowDialog()
        if ($outcome -eq [System.Windows.Forms.DialogResult]::OK) {
            return $form.Tag
        }
        return $null
    } finally { $form.Dispose() }
}

function Save-OwnerShellPaths {
    param($Settings, $User, [string]$ConfigPath, [bool]$ReplaceExisting)
    $directory = Split-Path -Parent $ConfigPath
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        $null = New-Item -ItemType Directory -Path $directory -Force
    }
    $temp = Join-Path $directory ('setup-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temp, ($Settings | ConvertTo-Json -Depth 3),
                                [Text.UTF8Encoding]::new($false))
        $acl = Get-Acl -LiteralPath $temp
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetOwner($User.User)
        $acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $User.User, [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow))
        Set-Acl -LiteralPath $temp -AclObject $acl -ErrorAction Stop
        $null = Assert-PrivateFile $temp 'newConfig' $true
        if ($ReplaceExisting) {
            $null = Assert-PrivateFile $ConfigPath 'config' $true
            # Preserve the old protected file if the atomic replacement fails.
            $backup = Join-Path $directory ('prior-' + [guid]::NewGuid().ToString('N') + '.json')
            [IO.File]::Replace($temp, $ConfigPath, $backup)
            $null = Assert-PrivateFile $ConfigPath 'config' $true
            if (Test-Path -LiteralPath $backup) {
                Remove-Item -LiteralPath $backup -Force -ErrorAction Stop
            }
        } else {
            if (Test-Path -LiteralPath $ConfigPath) { throw 'OWNER_SHELL_CONFIG_ALREADY_EXISTS' }
            Move-Item -LiteralPath $temp -Destination $ConfigPath -ErrorAction Stop
            $null = Assert-PrivateFile $ConfigPath 'config' $true
        }
    } finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
        }
    }
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
    # This reviewed Mirror revision contains Browser UI changes, but the
    # Owner Shell launcher, its Python module and its package initializer are
    # byte-for-byte unchanged. Admit only this exact source revision; NEVER
    # use an allow-all diff or pin the entire changing mirror_api tree.
    $approvedBase = 'a5cb3e9f61e68331c11337eb5da4af056d27660c'
    $approvedMirror = 'c0f5db887f29996ee3464cf991674c31d0a22eaf'
    $sha = (& $git -C $repo rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sha -cnotmatch '^[0-9a-f]{40}$') {
        throw 'OWNER_SHELL_SOURCE_HEAD_INVALID'
    }
    & $git -C $repo merge-base --is-ancestor $approvedBase $sha | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'OWNER_SHELL_SOURCE_BASE_MISMATCH' }
    if ($sha -cne $approvedMirror) { throw 'OWNER_SHELL_SOURCE_PIN_CHANGED' }
    # Python module invocation: py -3 -m mirror_api.owner_shell. The module
    # imports standard-library modules only. Pin its exact Git blob and the
    # package initializer, separately from unrelated Browser/READ modules.
    $sourceBlobs = [ordered]@{
        'Start-OwnerPowerShellExecutor.ps1' = '36798dbc1c48829eb395b08128618ac3c8962b57'
        'mirror_api/__init__.py' = '9cebe46e8c113d55d1aca036cca594a2f2853b56'
        'mirror_api/owner_shell.py' = '166983065044257a20cc9252fbe6859c44ccdff4'
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
    $exists = Test-Path -LiteralPath $configPath -PathType Leaf
    $data = $null
    if ($exists) {
        $config = Assert-PrivateFile $configPath 'config' $true
        $data = Get-Content -LiteralPath $config -Raw -Encoding UTF8 |
            ConvertFrom-Json -ErrorAction Stop
        if (@($data.PSObject.Properties.Name).Count -ne $names.Count -or
            @($names | Where-Object { $data.PSObject.Properties.Name -cnotcontains $_ }).Count -ne 0) {
            throw 'OWNER_SHELL_CONFIG_SCHEMA_INVALID'
        }
    }
    if (-not $exists -or $Configure) {
        if (@(Get-NetTCPConnection -LocalPort 10443 -State Listen -ErrorAction SilentlyContinue).Count -ne 0) {
            throw 'OWNER_SHELL_PORT_IN_USE_DO_NOT_DUPLICATE'
        }
        $settings = Show-OwnerShellSetup $data
        if ($null -eq $settings) {
            Write-Host 'OWNER_SHELL_SETUP_CANCELLED'
            return
        }
        Save-OwnerShellPaths $settings $user $configPath $exists
        Write-Host 'OWNER_SHELL_PATHS_CONFIGURED=YES'
        Write-Host 'OWNER_SHELL_STARTED=NO'
        Write-Host 'Paths saved securely. Launch the catalog entry again to start Executor.'
        return
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
    $message = 'OWNER_SHELL_SCRIPT_CENTER_STOP: ' + $_.Exception.Message
    [Console]::Error.WriteLine($message)
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        [void][System.Windows.Forms.MessageBox]::Show(
            $message, 'Fortis Windows Executor — запуск остановлен',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error)
    } catch {
        # Keep the original error as the authoritative outcome.
    }
    exit 1
}
