# Script Control Center

Source for the **Windows Fortis / RDG Control Center** and its user-script catalog. The repository contains a **source-only candidate** imported from the Fortis Repo workspace on 2026-11-01; it is **not** evidence that the updated version is installed or verified on Windows.

## What it does

- `Start-ScriptCatalog.ps1` opens the independent script catalog UI (without starting the Fortis/RDG service cards).
- `Start-ControlCenter.ps1` opens the combined Fortis/RDG Control Center and catalog.
- `ScriptCatalog.cs` implements the script registration, status and log-view UI; `Script-Runner.ps1` runs supported registered commands in the configured mode.
- The `Repo*-Worker.ps1`, `Updater-Worker.ps1`, and `Watcher-*.ps1` files are **Windows adapters**, not portable launchers or permission to change working infrastructure.
- `BUNDLE-MANIFEST.md` documents the optional preset and external installed-file dependencies. Importing a preset is registration-only and does not launch the workers.

## Canonical Git-to-Windows source (transition)

The intended workflow is **Docker Git checkout → commit/push → Windows clone fast-forward pull → manual acceptance → explicit run**. The Desktop shortcut should target `Start-ControlCenter.ps1` in the Windows Git clone, not `%LOCALAPPDATA%` copies. The bundled workers now use relative `launchers/` paths for Repo 1/2 and shared Fortis Guard. Runtime credentials and profiles stay local to Windows; the RDG updater itself is still an external dependency. Source checkout does **not** restart a live tunnel or prove cold-start compatibility.

See [Git-to-Windows operating procedure](docs/GIT-WINDOWS-SOURCE-OF-TRUTH.md) and `tools/Update-Windows-ControlCenter.ps1` (read-only preview by default). **After changing Docker**, update the corresponding resource/pin checks and verify both launchers before terminating the last working Fortis Repo connection. Repo 2 is not yet confirmed to reconnect using the cloned-source launcher.

## Зеркальная директория репозиториев (Docker ↔ Windows)

Это **логическое зеркало через GitHub**, а не одна общая папка и не Docker bind mount:

| Роль | Путь |
| --- | --- |
| Исходники в Docker | `/workspace/repo/Repos/<repo>` (тот же volume-backed каталог, что `/workspace/repos/<repo>`) |
| Удалённый источник обмена | `https://github.com/AlexPastukhh/<repo>.git` |
| Windows-клон | `%USERPROFILE%\Repos\<repo>` |

Для этого приложения соответствующая папка Windows — **`C:\Users\alexa\Repos\script-control-center`**. Используем **уже принятую Windows-папку `Repos`**, а не вторую копию `%USERPROFILE%\repos` в документации (Windows не различает эти имена по регистру), не `%LOCALAPPDATA%\FortisRDGControlCenter` и не Docker volume. Новые файлы в Docker не появляются автоматически на Windows: после коммита и `git push` владелец выполняет `git pull --ff-only` в нужном Windows-клоне. Незакоммиченные Docker-файлы не зеркалируются.

Первое создание Windows-клона (в **обычном Windows PowerShell**, не через Docker MCP):

```powershell
$mirror = Join-Path $env:USERPROFILE 'Repos'
$repo = Join-Path $mirror 'script-control-center'
if (Test-Path -LiteralPath $repo) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo '.git'))) {
        throw "Каталог существует, но это не Git-клон: $repo"
    }
    Write-Host "Git-клон уже существует: $repo"
} else {
    New-Item -ItemType Directory -Path $mirror -Force | Out-Null
    git clone --branch main --single-branch 'https://github.com/AlexPastukhh/script-control-center.git' $repo
    if ($LASTEXITCODE -ne 0) { throw 'Клонирование не удалось' }
}
git -C $repo remote -v
git -C $repo rev-parse HEAD
```

Команда **только создаёт или проверяет клон**. Отдельная проверка и смена ярлыка — `tools/Update-Windows-ControlCenter.ps1` (`-Apply` только после просмотра его действий). Не удалять существующие другие репозитории из `Repos`; не путать Windows-клон с `/workspace/repo/Repos`.

## Requirements and verification

The UI requires **Windows PowerShell 5.1, Windows Forms and an STA thread**. It is not a Linux-native application. The existing Docker workspace can edit the source and run the static Python regression tests, but it cannot verify real WinForms compilation or GUI behavior.

```bash
python3 -B test_static_catalog.py
```

On a **separate Windows test copy**, with explicit approval and without touching running Fortis/RDG sessions:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Test-ScriptCatalogCompile.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Test-CatalogOnly.ps1
```

Do not run or install the Fortis/RDG workers as part of this test. A successful source test is **not** permission for production rollout, service restarts, or live worker execution.

## Source history

The candidate was copied byte-for-byte from:
`/workspace/repo/Workfiles/fortis-rdg-control-center-source/working-copy-20261031-123027-8aff3e9e/` (Fortis Repo Docker persistent volume). The earlier Windows source snapshot is preserved separately in that same Workfiles source bundle; this Git repository tracks the reviewed working candidate, not Windows-installed state.

See [modernization notes](docs/MODERNIZATION-README.md) and the [2026-11-01 source audit](docs/history/AUDIT-20261101.md). Historical documents describe the original snapshot and may say it was not yet in Git; that was true at the time of the audit.

Infrastructure ownership: the `tunnel` repository owns the Fortis Repo/Tunnel safety and connection documentation. This repository owns the script-control-center **source**, not the Windows installation or its runtime credentials.

## Security and local state

Do not commit credentials, personal paths, live process manifests, catalog databases, generated logs, secrets, Windows shortcuts, or runtime configuration. Registered scripts are arbitrary code with the launcher's Windows privileges. Adding a script to the catalog does **not** establish that the script is safe. A separate owner-controlled acceptance is required for any Windows installation or live process change.
