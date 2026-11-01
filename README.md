# Script Control Center

Source for the standalone **Windows script catalog** and an optional combined Fortis/RDG Control Center. The five built-in worker entries point to this stable Windows Git clone. Repo 1/2 and shared Fortis adapters now resolve their executable worker/launcher source only from the sibling `%USERPROFILE%\Repos\tunnel` clone, verify its pinned manifest and all eight SHA-256 values, and fail closed if that clone is missing, dirty, off `main`, or stale. This source change is **not** proof of a successful Windows reconnect.

## What it does

- **`Start-ScriptCatalog.ps1` is the primary application and Desktop shortcut target.** It opens the standalone catalog directly, without the old four-card shell.
- `Start-ControlCenter.ps1` is the optional older combined four-card shell with a «Скрипты…» button. **It is not the primary Desktop shortcut target.**
- `ScriptCatalog.cs` implements the script registration, status and log-view UI; `Script-Runner.ps1` runs supported registered commands in the configured mode.
- The `Repo*-Worker.ps1`, `Updater-Worker.ps1`, and `Watcher-*.ps1` files are **Windows adapters**, not portable launchers or permission to change working infrastructure.
- `BUNDLE-MANIFEST.md` documents the optional preset and external installed-file dependencies. Importing a preset is registration-only and does not launch the workers.

## Canonical Git-to-Windows source (transition)

The intended workflow is **Docker Git checkouts → commit/push both repos → Windows fast-forward pull of both sibling clones → read-only preflight → controlled per-tunnel restart**. The Desktop `Fortis RDG Control Center.lnk` shortcut targets **`Start-ScriptCatalog.ps1`** in `%USERPROFILE%\Repos\script-control-center`, not `Start-ControlCenter.ps1` or `%LOCALAPPDATA%` copies. The catalog's Repo1/Repo2 adapters call `Get-TunnelWindowsMirror.ps1`, which accepts only `%USERPROFILE%\Repos\tunnel\runtime\windows-repo\current`, checks both canonical clean `main` clones and the pinned eight-file manifest, then delegates to the worker chain there. Updater/Watcher adapters resolve shared `Common.ps1` / `Start-Fortis.ps1` from the same tunnel mirror. The old `script-control-center\launchers` copies are retained for source history/contract tests, **not used by the Script Center workers**. Runtime binaries, credentials, profiles and logs stay in their documented Windows-local locations; the RDG updater remains an external dependency. Pulling source does **not** restart a live tunnel or prove reconnect compatibility.

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

## Docker → GitHub → Windows: branch-aware workflow inspector (new default)

The primary `Fortis-Windows-Mirror-Status.ps1` GUI now loads
`Mirror-Workflow.UI.cs`. The original main-only `Mirror-Status.UI.cs` is retained
as historical/source reference, and `-Cli` still runs the old text-only
`Mirror-Status.Report.ps1` (main-only). The new UI is **Windows PowerShell 5.1 / WinForms**,
not JavaScript. It uses the Docker-first read-only exporter
`tools/Export-Docker-Branch-Workflow.py` inside `fortis-repo-test`; keep that
exporter in the Docker Git checkout. **Nothing was deployed to Windows by
editing the Docker source.**

**Source of truth and screen:** the table enumerates Git repositories under
Docker `/workspace/repo/Repos/` (not Windows folders), one row per repository.
If Docker is unavailable, it shows an error instead of a misleading empty list.
Windows clone is expected under `%USERPROFILE%\Repos\<docker-folder-name>`;
missing clones remain visible as `НЕТ КЛОНА WINDOWS`. Windows-only folders are
not added. The UI shows a compact toolbar on top, a full-width ListView with
13 columns separated by `Docker · ...`, `Windows · ...`, and `Workflow · ...`,
then a multiline scrollable full-detail panel **below** the table. Every table
column is represented as an untruncated detail line in the identical order.
Both `Другие ветки Docker` and `Другие ветки Windows` show the same 13 full
fields for each other branch after the selected branch's primary details.

**Two independent branch selectors per environment:** `Ветка для просмотра`
changes only the branch whose SHA/status is displayed; **no Git checkout**.
`Активная ветка` always reports actual `symbolic-ref HEAD` from that environment;
clicking it offers local existing branches for an explicit Git switch (with
confirmation and fresh preflight). Switching does **not** change the branch
selected for viewing. The overall workflow state is always derived from the
actual active Docker and Windows branches, never from their view selectors.
All-`main` clean equality is green `АКТУАЛЕН`; equality on a non-main branch is
`АКТУАЛЕН · НЕ MAIN` (warning rather than error). It also distinguishes
Docker unstaged/uncommitted files, Docker commits ahead of GitHub, Windows
behind, unexpected Windows commits ahead (error), divergent history, missing
Windows clones and unknown/unverified state. A different SHA alone is **not**
called ahead or behind. On user-triggered Check, GitHub branches are obtained
via verified `ls-remote --heads`; Docker ancestry is checked in its Git object
database with exact GitHub SHA and Windows ancestry in the Windows clone. If
required Git objects are missing, direction remains `НЕИЗВЕСТНО` instead of
guessed. Windows may fetch **Git metadata only** for its active branch to get
exact counts, without changing worktree files or its checked-out branch.

**Operating behavior:** opening the window reads the Docker inventory only;
remote GitHub/Windows checks require `Проверить выбранные` or `Проверить все`.
Checks execute off the UI thread and can be cancelled. Status and row
contents refresh **after each repository completes** (GitHub, Windows and the
Docker ancestry check), showing `Проверяется N из M` / `Проверено N из M`
during the run instead of waiting for the final repository. On cancellation,
already completed rows remain visible; unprocessed rows retain their previous
state. Choosing the already-active branch in either `Активная ветка` selector
is a no-op with no Git call. The branch-menu cleanup is deferred until after
its WinForms close/click handlers return. Docker read-only export never
fetches, pushes or changes a worktree. The owner-only actions are:

- `Stage Docker` opens a checklist of current **active Docker** working-tree
  changes (nothing selected by default). Only checked Git-reported paths are
  staged with literal Git pathspecs. The preview includes an SHA-256 digest of
  the complete NUL-delimited porcelain status; Stage rechecks active branch,
  HEAD and that exact digest before writing. No blanket `git add -A` and no
  Windows Git write. For safety, more than 500 changed entries require direct
  owner-controlled Docker Git instead of using the GUI.
- `Commit Docker` previews **all currently staged/indexed files**, asks for an
  explicit message (UTF-8; may contain newlines), and requires confirmation.
  The helper checks the SHA-256 of the raw staged index diff plus active
  Docker branch and HEAD before running normal `git commit` with hooks and
  signing disabled. A Commit does not stage remaining unstaged files. Changes
  appearing between preview and commit invalidate the digest.
- `Push Docker` previews Docker HEAD and the exact current GitHub branch SHA
  via `ls-remote`, then requests confirmation. It rechecks active branch,
  HEAD and GitHub SHA; pushes **only** `HEAD:refs/heads/<active-branch>`
  using standard non-forcing `git push`. No force, automatic Windows Pull,
  branch switch, tagging or service restart. Uncommitted working-tree files
  are never included in a Push. A not-yet-published branch requires its own
  confirmation as part of the same Push dialog. Authentication errors stop
  the operation rather than prompting through the UI.
- These three buttons call `tools/Docker-Mirror-Git-Actions.py` **inside**
  `fortis-repo-test` as uid 1000; the Windows process is a UI/controller only.
  The operation target is always the **actual active Docker branch**, not
  the branch selected for viewing. Stage/Commit/Push are blocked for protected
  infrastructure names (`tunnel`, `rd-graphs`, `script-control-center`,
  `security`) in this workflow, to avoid changing service-critical
  repositories through a general status UI. Use a deliberate Docker
  development/review workflow to modify those repositories.
- `Pull Windows` only for one verified clean Windows clone, the currently
  active **and viewed** Windows branch, approved Docker/Windows origin, and
  confirmed `WindowsAhead == 0`, `WindowsBehind > 0`. It confirms the exact
  displayed GitHub SHA, repeats GitHub/Windows preflight, fetches the selected
  branch, checks `FETCH_HEAD` still equals that SHA, checks HEAD/branch/clean
  worktree again, and performs **`git merge --ff-only FETCH_HEAD`**. The button
  is called Pull but uses exact-SHA fetch + fast-forward merge to prevent
  accidentally importing a newer unreviewed GitHub commit. No forced reset,
  stash, force-push, auto-restart or deploy. Configured Git file filters may
  still run on Git writes.
- Windows branch switch requires clean tree and an existing **local** target
  branch, approved matching remote, user confirmation, and a fresh branch,
  HEAD, dirty-status and target SHA check; command is `git switch --no-guess`.
- Docker branch switch is **explicit owner action**, never automatic, and
  requires a clean Docker Git tree, existing local target branch, owner
  confirmation and reverified active branch/HEAD via exporter `--switch-*`.
  Switching the shared infrastructure repositories `tunnel`, `rd-graphs`,
  `script-control-center` and `security` is blocked in the GUI/exporter.
  It can still affect files used by other running programs; do not treat the
  GUI confirmation as approval to alter protected live infrastructure.

**Windows is a receiver, not an authoring environment:** no Windows commit-all
or push button in the new default screen. Unexpected local edits or Windows
commits ahead of GitHub stop the normal workflow. Source updates do not
restart any running Fortis/RDG services. The historical TSV import and
old main-only Windows commit-all remain in the unused legacy UI source; the
new GUI does not import old `FORTIS_DOCKER_STATUS_V1` TSV files. The new
Docker branch snapshot is `FORTIS_DOCKER_BRANCH_V2` (name, active branch,
HEAD, dirty count, local branches, approved origin) and is not stored in Git.
In addition to the unchanged 13 column-detail lines, the details area lists
changed files **separately for the active Docker and Windows worktrees**, below
`ДОПОЛНИТЕЛЬНЫЕ СВЕДЕНИЯ`. Each item includes the Git porcelain status
(` M` = modified, `A ` = staged addition, ` D` = deletion, `??` = untracked,
etc.) and its path. The Docker exporter carries each status record as a
base64-encoded `F` line, so tabs, spaces and Git-quoted filenames do not
corrupt the snapshot. Windows reads `git status --porcelain=v1` locally,
without changing files. Both lists describe the **currently active branches**
regardless of which branches are selected for viewing. File lists are
limited to the first 500 entries per environment/repository; if there are
more entries or a status read fails, the UI explicitly marks the list as
partial/unavailable instead of reporting a clean worktree.

**Verification:** offline Linux source/fixture tests and Docker exporter can be
checked without touching live state:

```bash
python3 -B -m unittest test_mirror_docker_actions test_mirror_workflow_source test_mirror_status_source test_mirror_git_history
python3 -B tools/Export-Docker-Branch-Workflow.py --stdout --repo visual-workspace
```

For **real Windows WinForms compilation**, run from the reviewed Windows Git
clone only after owner-approved source propagation:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Test-MirrorWorkflowCompile.ps1
```

The compiler test does not open the UI, check GitHub, run Docker, switch
branches or pull. Windows UI compilation and end-to-end owner acceptance
remain **not verified from the Docker environment**. Treat the newly
implemented UI as a source candidate until those tests pass.

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
