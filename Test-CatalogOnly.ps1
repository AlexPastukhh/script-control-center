#requires -Version 5.1
# Isolated GUI smoke test: ONLY ScriptCatalogForm. Does not create MainForm or call four workers.
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows required' }
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Use powershell.exe -NoProfile -STA -File .\Test-CatalogOnly.ps1'
}

$root = $PSScriptRoot
$catalog = Join-Path $root 'ScriptCatalog.cs'
$runner = Join-Path $root 'Script-Runner.ps1'
if (-not (Test-Path -LiteralPath $catalog -PathType Leaf)) { throw 'ScriptCatalog.cs missing' }
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) { throw 'Script-Runner.ps1 missing' }

$testDir = Join-Path $env:TEMP ('fortis-script-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir -ErrorAction Stop | Out-Null
$testScript = Join-Path $testDir 'safe-output.ps1'
$sample = @'
# Safe smoke test: outputs timestamps for 30 seconds; does not touch Docker or Fortis/RDG.
1..30 | ForEach-Object {
    Write-Output ("SMOKE tick {0} of 30 at {1}" -f $_, (Get-Date -Format 'HH:mm:ss'))
    Start-Sleep -Seconds 1
}
'@
[IO.File]::WriteAllText($testScript, $sample, (New-Object Text.UTF8Encoding($true)))

Add-Type -Path $catalog -ReferencedAssemblies @(
    'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll',
    'System.Drawing.dll', 'System.Management.dll', 'System.Xml.dll'
) -ErrorAction Stop | Out-Null

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()

Write-Host ('TEST_SCRIPT_PATH=' + $testScript)
Write-Host 'Only the user script catalog will open. The 4 original service cards are not started.'
Write-Host 'Inside the window: Add -> select safe-output.ps1 -> select "Inside center" -> Start.'
Write-Host 'Confirm that SMOKE tick messages appear, then test Stop and reopen.'
$window = [FortisRdgControl.ScriptCatalogForm]::new($root)
try { [void]$window.ShowDialog() } finally { $window.Dispose() }
