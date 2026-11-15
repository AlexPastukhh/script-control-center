# Script Control Center

Source for the standalone **Windows script catalog** and an optional combined Fortis/RDG Control Center. The five built-in worker entries point to this stable Windows Git clone. Repo1/Repo2 adapters resolve executable worker/launcher source only from the sibling `%USERPROFILE%\Repos\tunnel` clone, verify its pinned nine-file manifest and fail closed if either clone is missing, dirty, off `main`, or stale. The canonical Tunnel runbook records Windows acceptance; source hashes alone never prove a live reconnect.

## Локальные Use Cases

[Реестр Script Center](docs/use-cases/README.md) — Script Catalog и paired
Windows update. Для Docker task → GitHub → Windows обязательны общие
[UC-CONT-002/003](https://github.com/AlexPastukhh/fortis-container/blob/main/docs/use-cases/README.md)
и правило чистого основного `main` [UC-CONT-019](https://github.com/AlexPastukhh/fortis-container/blob/main/docs/use-cases/worktrees.md#uc-cont-019).
**Не редактировать `Repos/script-control-center`; изменения вести только в
`Worktrees/script-control-center/<task>` и публиковать через Gateway/PR.**

## What it does

- **`Start-ScriptCatalog.ps1` is the primary application and Desktop shortcut target.** It opens the standalone catalog directly, without the old four-card shell.
- `Start-ControlCenter.ps1` is the optional older combined four-card shell with a «Скрипты…» button. **It is not the primary Desktop shortcut target.**
- `ScriptCatalog.cs` implements the script registration, status and log-view UI; `Script-Runner.ps1` runs supported registered commands in the configured mode.
- The `Repo*-Worker.ps1`, `Updater-Worker.ps1`, and `Watcher-*.ps1` files are **Windows adapters**, not portable launchers or permission to change working infrastructure.
- `BUNDLE-MANIFEST.md` documents the optional preset and external installed-file dependencies. Importing a preset is registration-only and does not launch the workers.

## Ownership and canonical Windows source

This repository owns the Script Catalog/Control Center UI, the five catalog registrations, thin worker adapters, `Get-TunnelWindowsMirror.ps1` resolver and paired Windows updater. It does **not** own the Repo1/Repo2 security policy or the canonical worker/launcher implementation.

The complete Repo1/Repo2 worker chain and the one shared Guard policy are owned by [`tunnel/runtime/windows-repo/current`](https://github.com/AlexPastukhh/tunnel/tree/main/runtime/windows-repo/current). Shared policy: `launchers/shared/FortisContainerPolicy.ps1`. Repo1/Repo2 retain only their profile-specific tunnel, key/ACL and quiet-launch checks. The old `script-control-center\launchers` tree is retained for source history/contract tests, not used by the active workers.

The resolver requires both canonical sibling Windows Git clones on clean, synchronized `main`, verifies the approved origins and all nine manifest-pinned file hashes, then delegates to the Tunnel worker chain. Missing, dirty, stale or modified source fails closed; there is no local-launcher fallback.

The paired updater is Preview by default and does not restart workers or import
registrations. Its complete Windows procedure, checks and shortcut behavior
belong to [GIT-WINDOWS-SOURCE-OF-TRUTH](docs/GIT-WINDOWS-SOURCE-OF-TRUTH.md),
not a second instruction set in this README.

## Docker / Windows Git clones

| Роль | Docker source | Windows clone |
|---|---|---|
| Script Catalog, UI и adapters | `/workspace/repo/Repos/script-control-center` | `%USERPROFILE%\Repos\script-control-center` |
| Repo1/Repo2 workers и shared Guard | Git repo `tunnel` → `runtime/windows-repo/current` | `%USERPROFILE%\Repos\tunnel\runtime\windows-repo\current` |

Это **отдельные Git-клоны**, не общая папка и не bind mount. GitHub — канал передачи: Docker edit → commit/push → Windows `git pull --ff-only`. Незакоммиченные изменения и runtime state не синхронизируются. Не создавать вторые source-копии в `%LOCALAPPDATA%` и не копировать Git-файлы вручную.

Создание отсутствующих Windows-клонов, проверка существующих путей и безопасное обновление обоих репозиториев описаны в [docs/GIT-WINDOWS-SOURCE-OF-TRUTH.md](docs/GIT-WINDOWS-SOURCE-OF-TRUTH.md). Для routine update используй `tools/Update-Windows-ControlCenter.ps1` (Preview по умолчанию, `-Apply` после проверки); не reimport-ить пять catalog registrations после pull.

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

## Профили зарегистрированных скриптов

В standalone Script Catalog кнопка **«Профили»** открывает список профилей и
зарегистрированных скриптов с флажками. Создайте профиль, отметьте нужные записи
и нажмите **«Сохранить состав»**. Можно создавать, переименовывать и удалять
профили; удаление профиля **не** удаляет регистрацию и не завершает процессы.

**«▶ Запустить профиль»** и **«■ Остановить профиль»** требуют одного
подтверждения на всю группу и обрабатывают записи независимо. Уже работающий
скрипт при запуске пропускается, а уже остановленный — при остановке.
Неоднозначные внешние PowerShell процессы и недостоверная WMI-проверка
блокируют действие только для соответствующей записи; итоговое окно показывает
пропуски, ошибки и запросы остановки. Остановка в режиме «Внутри центра»
асинхронная (stop-token), и сообщение «остановка запрошена» не означает,
что процесс уже завершён. Снаружи запущенный PowerShell останавливается только
после точной повторной проверки PID/владельца/сессии/пути.

Профили хранят **ID существующих регистраций** (не копии команд) в
`%LOCALAPPDATA%\FortisRdgControlCenter\scripts\profiles.xml` с отдельными
резервными копиями. Переименование скрипта сохраняет привязку; удалённая
регистрация остаётся отмеченной как отсутствующая, пока состав профиля не
пересохранён. Никаких автоматических запусков при открытии каталога нет.

## FileQuarantine launcher (manual registration only)

The standalone `FileQuarantine-Launcher.ps1` is a **manual, opt-in** Script
Catalog entry. It is not part of the default five-item preset, is never
registered automatically and does not alter saved catalog data.

After pulling the Script Center `main` on Windows, open the Script Catalog,
click **«＋ Добавить»**, choose:
`%USERPROFILE%\Repos\script-control-center\FileQuarantine-Launcher.ps1`,
set the name **«FileQuarantine — запуск»**, leave arguments empty and choose
**«Внутри центра»** (for captured preflight errors and a managed process) or
**«Своё окно»**, then save. Select the entry and click **«▶ Запустить»**.

The adapter delegates to the existing sibling Windows clone at
`%USERPROFILE%\Repos\FileQuarantine` and runs its `00-Preflight.ps1` before
launching `FileQuarantine-GUI.ps1` in Windows PowerShell 5.1 STA mode. It
**does not** clone or pull Git, build images, create/delete Docker Volumes,
start Tunnel, install anything, or bypass FileQuarantine's normal safety check.
If an image such as `fileq/http-downloader:auto-name-v1` is absent, install it
separately using FileQuarantine's documented setup process. This adapter
reports failed preflight to the catalog log rather than pausing a CMD window.

## Docker → GitHub → Windows: Mirror application (standalone source)

**Mirror UI, Docker exporters, Git actions, legacy source, tests and detailed
documentation are owned by** [`AlexPastukhh/docke-windows-mirror`](https://github.com/AlexPastukhh/docke-windows-mirror).
The Docker Git checkout is `/workspace/repo/Repos/docke-windows-mirror`;
the separate Windows Git clone is `%USERPROFILE%\Repos\docke-windows-mirror`.

For the accepted **read-only Browser UI**, use the separate Script Center
launcher **`Fortis-Mirror-Browser.ps1`**. It validates the sibling Windows
`docke-windows-mirror` clone, correct GitHub origin and active `main`, then
calls the Mirror-owned `Start-MirrorAPI.ps1 -OpenBrowser`. The server binds
only to `127.0.0.1:8765`; it opens the one-use login URL in the default browser.
The owning PowerShell window must remain open while the UI is in use.
If the browser does not open automatically, copy the one-use URL printed in
that window. `Ctrl+C` stops the server.

**Registration is an owner-local Script Catalog action**, not a Git commit:
first update *both* Windows clones to their approved merged `main`. In
Script Catalog select **«Импорт из текста»** and paste the non-comment line
from `TEXT-IMPORT-MIRROR-BROWSER.txt`, then **«Проверить» →
«Импортировать»**. Use mode **`window` / «Своё окно»**, not hidden, so
startup errors and the session URL are visible. The entry is named
**«Fortis Mirror — браузер»**; it is separate from the retained WinForms entry.
The text importer rejects duplicates by path/arguments and never starts code.
Neither registration nor launch performs any Git Pull/clone, Docker changes,
Gateway activation, Windows Pull, or service installation.

The stable **legacy Script Center entry** path remains
Fortis-Windows-Mirror-Status.ps1: after the approved Windows source update,
its default GUI launch now delegates to Fortis-Mirror-Browser.ps1 and the
one-use loopback session. Explicit -Cli / -Notify still invoke the
retained legacy reporter with the approved GitHub origin check. Browser UI
source remains exclusively in the sibling Windows Mirror repository.

For an **already saved** legacy Script Catalog entry, select that record,
click **«Изменить»**, set **mode window** («Своё окно»), and save. This is
necessary to see the one-time login URL and any startup error when opening the
Browser. First bring the approved script-control-center main into the
separate Windows clone. The stable filename is unchanged: no record
deletion/import needed. There is **no automatic catalog migration**, Git
clone/pull, service restart, TLS listener or Gateway activation.

Installation and approved-source delivery belong to the
[Mirror README](https://github.com/AlexPastukhh/docke-windows-mirror/blob/main/README.md)
and [Windows sync UC](https://github.com/AlexPastukhh/docke-windows-mirror/blob/main/docs/use-cases/windows-sync.md#uc-mir-002).
This repo owns only the thin compatibility launcher; it does not perform clone,
pull or deployment as a side effect of launching it.

## Optional Mirror listener adapters and reviewed source pin

`Fortis-Owner-PowerShell-Executor.ps1` and `Fortis-Mirror-Gateway-Read.ps1`
are manually registered, independent `window` entries, not part of the normal
Mirror Browser. Both require the **exact reviewed** clean Windows Mirror
`main` commit `c0f5db887f29996ee3464cf991674c31d0a22eaf`.
The Owner Executor separately pins its unchanged PowerShell launcher and
Python owner-shell module; Gateway READ pins its listener **and** changed
read-only observation module. Later Mirror commits must be reviewed and pinned
explicitly. Neither adapter installs a listener or performs Git updates as
part of registration; the source update alone does not activate either one.
See [Executor](docs/OWNER-POWERSHELL-EXECUTOR-LAUNCH.md) and
[Gateway READ](docs/MIRROR-GATEWAY-READ-LAUNCH.md) for required credentials,
owner-only setup and runtime restrictions.

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


## Будущие улучшения

Единый Script Center backlog: [BACKLOG-IMPROVEMENTS](docs/BACKLOG-IMPROVEMENTS.md).
Он не разрешает Windows или production-мутации.
