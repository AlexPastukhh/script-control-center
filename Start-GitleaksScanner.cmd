@echo off
rem Script Catalog "window" entry: no PowerShell execution-policy override.
rem If local policy forbids unsigned scripts, fail closed; do not bypass it.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -File "%~dp0Start-GitleaksScanner.ps1"
exit /b %ERRORLEVEL%
