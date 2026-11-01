# Script Control Center

Source for the standalone **Windows script catalog** and an optional combined Fortis/RDG Control Center. On 2026-11-01 the owner confirmed that the standalone catalog opens from the Windows Git clone and that five built-in worker entries were re-pointed to that clone. This confirms the catalog UI, **not** a successful Repo 1/2 reconnection from the cloned launchers.

## What it does

- **`Start-ScriptCatalog.ps1` is the primary application and Desktop shortcut target.** It opens the standalone catalog directly, without the old four-card shell.
- `Start-ControlCenter.ps1` is the optional older combined four-card shell with a «Скрипты…» button. **It is not the primary Desktop shortcut target.**
- `ScriptCatalog.cs` implements the script registration, status and log-view UI; `Script-Runner.ps1` runs supported registered commands in the configured mode.
- The `Repo*-Worker.ps1`, `Updater-Worker.ps1`, and `Watcher-*.ps1` files are **Windows adapters**, not portable launchers or permission to change working infrastructure.
- `BUNDLE-MANIFEST.md` documents the optional preset and external installed-file dependencies. Importing a preset is registration-only and does not launch the workers.

## Canonical Git-to-Windows source (transition)

The intended workflow is **Docker Git checkout → commit/push → Windows clone fast-forward pull → manual acceptance → explicit run**. The Desktop `Fortis RDG Control Center.lnk` shortcut targets **`Start-ScriptCatalog.ps1`** in the Windows Git clone, not `Start-ControlCenter.ps1` or `%LOCALAPPDATA%` copies. The bundled workers now use relative `launchers/` paths for Repo 1/2 and shared Fortis Guard. Runtime credentials and profiles stay local to Windows; the RDG updater itself is still an external dependency. Source checkout does **not** restart a live tunnel or prove cold-start compatibility.

See [Git-to-Windows operating procedure](docs/GIT-WINDOWS-SOURCE-OF-TRUTH.md) and `tools/Update-Windows-ControlCenter.ps1` (read-only preview by default, explicit `-Apply` for guarded pull and shortcut repair). The five registered workers retain absolute paths to the stable Windows clone; **do not reimport them after each Git pull**. **After changing Docker**, update the corresponding resource/pin checks and verify both launchers before terminating the last working Fortis Repo connection. Repo 2 is not yet confirmed to reconnect using the cloned-source launcher.

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

Команда **только создаёт или проверяет клон**. На текущей Windows-машине ярлык уже направлен на `Start-ScriptCatalog.ps1`, а пять регистраций обновлены до worker-файлов в клоне. Дальнейшее обновление после `git push`:

```powershell
$repo = Join-Path $env:USERPROFILE 'Repos\script-control-center'
git -C $repo pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw 'Git pull не завершился успешно' }
```

Повторно создавать ярлык и импортировать штатный набор после обычного `pull` **не нужно**. Новые файлы используются при следующем запуске каталога или отдельного worker-процесса, уже работающие процессы не заменяются. `tools/Update-Windows-ControlCenter.ps1 -Apply` предназначен для отдельной предварительной проверки/восстановления ярлыка и **не требуется при каждом обновлении**. Не удалять другие репозитории из `Repos`; не путать Windows-клон с `/workspace/repo/Repos`.

## Windows Git mirror inspector (checks + explicit selected pull)

The already-registerable `Fortis-Windows-Mirror-Status.ps1` now opens its own Windows Forms window when launched from the standalone Script Catalog. It immediately lists folders and Git clones under `%USERPROFILE%\Repos` (or an explicit `-MirrorRoot`), and provides **Проверить выбранный**, **Проверить все**, **Pull выбранного**, **Обновить список**, and **Отмена проверки**. Network checks run on a background worker with bounded waits, so the window remains usable. A non-Git directory is listed as such and is never queried.

Keep **all three files together** after a normal Windows `git pull --ff-only origin main`: `Fortis-Windows-Mirror-Status.ps1` (existing registration/GUI entry point), `Mirror-Status.UI.cs` (GUI source), and `Mirror-Status.Report.ps1` (original text report). The catalog entry already pointing to the first file does **not** need to be recreated. Run with no arguments to open the GUI; use `-Cli` for the previous console report (`-Cli -Notify` for its optional notification).

**Check** buttons call only read-only Git commands (`rev-parse`, `symbolic-ref`, `status`, `log`, `remote get-url`, `ls-remote`); no background auto-update, no bulk pull. The details pane shows the selected repository path, full local `main` SHA, date/subject of its last local commit and full remote `main` SHA. The remote commit *subject* cannot reliably be obtained by `ls-remote` alone before fetching; after a successful pull it becomes available locally. The explicit **Pull выбранного** action is enabled only when the remote SHA differs, the selected clone is clean, the current branch is `main`, and the GitHub origin has passed its allowlist check. It asks for approval and then repeats the read-only checks, verifying the previously displayed local/remote SHA before attempting `git pull --ff-only --no-rebase --no-recurse-submodules origin main`. A stale selection or changed SHA aborts. Git rejects non-fast-forward histories; no reset, force, auto-stash or automatic worker restart is performed. During the explicit pull the application does not allow the owner to close its window, to avoid accidentally terminating an in-progress Git update. Git hooks are disabled for that command; configured file filters may still run as part of Git. Pull is an **owner-triggered Windows source update**, not permission to execute or restart project software. For HTTPS GitHub requests it passes `-c http.sslBackend=openssl -c http.sslVerify=true` to that Git process only (the owner has previously observed Schannel errors on Windows); no global or per-clone Git configuration is changed. GUI labels distinguish TLS, authentication, DNS and network errors where recognizable, without displaying credential-bearing raw Git errors. If local `git status` times out, the remote `main` check still proceeds and the Changes column records the timeout. For network access it checks only a recognized `AlexPastukhh` GitHub origin and uses non-interactive Git settings. **АКТУАЛЕН** means the local `refs/heads/main` SHA matches GitHub; a different current branch or local edits are flagged in separate columns and in the review summary. **РАЗЛИЧАЮТСЯ** does not imply the next pull is safe. Windows PowerShell 5.1 + WinForms + git.exe are required. Windows runtime compilation/UI has **not** been verified inside Docker; the owner must test the first GUI launch on Windows.

## Requirements and verification

The UI requires **Windows PowerShell 5.1, Windows Forms and an STA thread**. It is not a Linux-native application. The existing Docker workspace can edit the source and run the static Python regression tests, but it cannot verify real WinForms compilation or GUI behavior.

```bash
python3 -B test_static_catalog.py
```

For a **source-only Windows compilation test**, without touching running Fortis/RDG sessions:

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
