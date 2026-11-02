# Script Control Center

Source for the standalone **Windows script catalog** and an optional combined Fortis/RDG Control Center. The five built-in worker entries point to this stable Windows Git clone. Repo 1/2 and shared Fortis adapters now resolve their executable worker/launcher source only from the sibling `%USERPROFILE%\Repos\tunnel` clone, verify its pinned manifest and all nine SHA-256 values, and fail closed if that clone is missing, dirty, off `main`, or stale. This source change is **not** proof of a successful Windows reconnect.

## What it does

- **`Start-ScriptCatalog.ps1` is the primary application and Desktop shortcut target.** It opens the standalone catalog directly, without the old four-card shell.
- `Start-ControlCenter.ps1` is the optional older combined four-card shell with a «Скрипты…» button. **It is not the primary Desktop shortcut target.**
- `ScriptCatalog.cs` implements the script registration, status and log-view UI; `Script-Runner.ps1` runs supported registered commands in the configured mode.
- The `Repo*-Worker.ps1`, `Updater-Worker.ps1`, and `Watcher-*.ps1` files are **Windows adapters**, not portable launchers or permission to change working infrastructure.
- `BUNDLE-MANIFEST.md` documents the optional preset and external installed-file dependencies. Importing a preset is registration-only and does not launch the workers.

## Canonical Git-to-Windows source (transition)

The intended workflow is **Docker Git checkouts → commit/push both repos → Windows fast-forward pull of both sibling clones → read-only preflight → controlled per-tunnel restart**. The Desktop `Fortis RDG Control Center.lnk` shortcut targets **`Start-ScriptCatalog.ps1`** in `%USERPROFILE%\Repos\script-control-center`, not `Start-ControlCenter.ps1` or `%LOCALAPPDATA%` copies. The catalog's Repo1/Repo2 adapters call `Get-TunnelWindowsMirror.ps1`, which accepts only `%USERPROFILE%\Repos\tunnel\runtime\windows-repo\current`, checks both canonical clean `main` clones and the pinned nine-file manifest (including the shared Repo1/Repo2 Guard policy), then delegates to the worker chain there. Updater/Watcher adapters resolve shared `Common.ps1` / `Start-Fortis.ps1` from the same tunnel mirror. The old `script-control-center\launchers` copies are retained for source history/contract tests, **not used by the Script Center workers**. Runtime binaries, credentials, profiles and logs stay in their documented Windows-local locations; the RDG updater remains an external dependency. Pulling source does **not** restart a live tunnel or prove reconnect compatibility.

See [Git-to-Windows operating procedure](docs/GIT-WINDOWS-SOURCE-OF-TRUTH.md) and `tools/Update-Windows-ControlCenter.ps1` (read-only by default; explicit `-Apply` fast-forwards `tunnel` first and `script-control-center` second, validates the mirror, compiles the WinForms sources, runs read-only Repo1/Repo2 Previews, then repairs the existing shortcut). The five catalog registrations retain their absolute paths to the stable Script Center clone; **do not reimport them after pulls**. Do not stop the Fortis Docker container as part of this source update: preserve it and restart Repo1/Repo2 Windows tunnel processes one at a time after both previews pass. Real reconnect is still unverified until Windows acceptance.

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

Команда **только создаёт или проверяет клон**. На текущей Windows-машине ярлык уже направлен на `Start-ScriptCatalog.ps1`, а пять регистраций обновлены до worker-файлов в клоне. Дальнейшее обновление после `git push` должно обновить **оба** соседних клона — сначала `tunnel`, затем `script-control-center`. Из Windows PowerShell можно выполнить:

```powershell
$repos = Join-Path $env:USERPROFILE 'Repos'
foreach ($name in @('tunnel','script-control-center')) {
    $repo = Join-Path $repos $name
    git -C $repo pull --ff-only origin main
    if ($LASTEXITCODE -ne 0) { throw "Git pull failed: $name" }
}
```

Либо запустить `tools\Update-Windows-ControlCenter.ps1 -Apply` из Script Center clone: он предварительно проверяет оба чистых `main` клона, обновляет `tunnel` первым, затем Script Center, проверяет восемь файлов зеркала и запускает только read-only Previews. Повторно создавать ярлык и импортировать штатный набор после pull **не нужно**. Уже работающие процессы не заменяются. Не удалять другие репозитории из `Repos`; не путать Windows-клоны с `/workspace/repo/Repos` в Docker.

## Скрипт-центр: стабильный журнал и внешние PowerShell-запуски

В окне **«Control Center — пользовательские скрипты»** журнал больше не
переписывается целиком при каждом обновлении (интервал 1,2 секунды).
Если пришёл новый текст, он добавляется в конец `RichTextBox`, а позиция
чтения сохраняется. Автопрокрутка происходит только тогда, когда пользователь
уже находится внизу. При ротации или сдвиге 90-КиБ окна журнала
обновление всего содержимого защищено от промежуточной перерисовки.

Кроме запусков из каталога, проверяются процессы **того же пользователя и
той же Windows-сессии**, непосредственно запущенные через
`powershell.exe -File "C:\полный\путь\script.ps1"`
или `pwsh.exe -File ...`. Фоновая проверка WMI выполняется
примерно раз в 6 секунд, отдельно от UI-потока. Имя процесса само по
себе не означает совпадение: сопоставляется **полный абсолютный путь
зарегистрированного `.ps1`**, а владелец и сессия сверяются с текущими.

- `Работает` — собственный подтверждённый runner / PID.
- `Работает (внешний)` — один точно совпавший внешний PowerShell-процесс;
  в деталях показаны PID и время старта, **но его stdout/stderr не доступны**.
- `Несколько внешних` — неоднозначно; остановка заблокирована.
- `Не проверен` — нет достоверного результата проверки WMI; это не «Остановлен».

**Остановить** для внешнего запуска требует отдельного подтверждения; PID,
время старта, владелец, сессия и путь `-File` перепроверяются непосредственно
перед завершением. Завершается только указанный PID PowerShell, без
`taskkill /T` и без остановки дочернего дерева. Несохранённая работа такого
скрипта может быть потеряна, поэтому завершение **никогда не происходит
автоматически**. Для процессов, запущенных самим каталогом, сохраняются
существующие механизмы: stop-token runner или подтверждённое дерево PID.

**Не обнаруживаются и не останавливаются автоматически** интерактивные
`& .\script.ps1`, `-Command`/`-EncodedCommand`, относительный путь в
`-File`, скрипты `cmd`/`python`, службы и процессы другого пользователя
или сессии. Для таких способов запуска нельзя надёжно определить безопасный
PID по полной командной строке без дополнительных механизмов сопровождения.
При ошибке WMI статус остаётся «Не проверен», не «Остановлен».

Компиляция standalone-каталога на Windows без запуска работников:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Test-ScriptCatalogCompile.ps1
```

## Docker → GitHub → Windows: Mirror application (standalone source)

**Mirror UI, Docker exporters, Git actions, legacy source, tests and detailed
documentation are owned by** [`AlexPastukhh/docke-windows-mirror`](https://github.com/AlexPastukhh/docke-windows-mirror).
The Docker Git checkout is `/workspace/repo/Repos/docke-windows-mirror`;
the separate Windows Git clone is `%USERPROFILE%\Repos\docke-windows-mirror`.

The stable launcher **remains here** at `Fortis-Windows-Mirror-Status.ps1`.
It delegates GUI and `-Cli` to the sibling Windows `docke-windows-mirror` clone,
checks the expected origin, and fails with an explicit instruction if that
clone is missing. There are no duplicate Mirror UI sources here. No automatic
Git clone, pull, process restart or service deployment is triggered by the launcher.

To install the separate Windows clone once, from Windows PowerShell:

```powershell
git clone https://github.com/AlexPastukhh/docke-windows-mirror.git "$env:USERPROFILE\Repos\docke-windows-mirror"
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "$env:USERPROFILE\Repos\docke-windows-mirror\Test-MirrorWorkflowCompile.ps1"
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Fortis-Windows-Mirror-Status.ps1
```

Update both Git clones with clean `git pull --ff-only` before launching.
See the [standalone README](https://github.com/AlexPastukhh/docke-windows-mirror/blob/main/README.md)
and its [`docs/WORKFLOW.md`](https://github.com/AlexPastukhh/docke-windows-mirror/blob/main/docs/WORKFLOW.md)
for workflow behavior and safety constraints.

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
