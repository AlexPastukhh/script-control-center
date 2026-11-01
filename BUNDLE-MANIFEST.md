# Fortis / RDG bundle manifest

## Worker files copied beside Control Center

The catalog importer resolves these files relative to the folder containing
`Start-ScriptCatalog.ps1` (the app's `$PSScriptRoot`):

| Display name | Worker file | Role |
|---|---|---|
| Fortis Repo 2 | `Repo2-Worker.ps1` | Start guarded Repo 2 session |
| Fortis Repo 1 | `Repo1-Worker.ps1` | Start Repo 1 independently via its own installed quiet launcher/guards |
| RDG Updater | `Updater-Worker.ps1` | Watch and apply an already-verified RDG build |
| CI Watcher — запуск | `Watcher-Start.ps1` | Ensure the Docker CI watcher is running |
| CI Watcher — остановка | `Watcher-Stop.ps1` | Stop only the matched CI watcher |

All five are registered in hidden/background mode with no arguments.
Import only registers file paths; it never runs a worker or contacts Docker.
Stable bundle IDs are stored in the catalog to prevent repeated imports.

## External dependencies checked by the import preview

These files are dependencies used by the worker scripts; they are not
separately registered as catalog entries.

| Worker | Required Windows file |
|---|---|
| Repo 2 | `%LOCALAPPDATA%\FortisRDGLauncherV2\Start-Fortis.ps1` |
| Repo 2 | `%LOCALAPPDATA%\FortisRepoPlus2\FortisRepo2-quiet-logs.ps1` |
| Repo 1 | `%USERPROFILE%\chatgpt-repo-mode\run-tunnel-quiet-logs.ps1` |
| RDG Updater | `%LOCALAPPDATA%\FortisRDGLauncherV2\Start-Fortis.ps1` |
| RDG Updater | `%LOCALAPPDATA%\RDG-Server\cicd-bootstrap-ci-20261029t051843803595-4546c7d69e\files\RDG-CICD-Bootstrap\Update-RDG.ps1` |
| CI Watcher start/stop | `%LOCALAPPDATA%\FortisRDGLauncherV2\Common.ps1` |

## Runtime requirements not exercised by import

- Repo 1 and Repo 2 are independently selectable in the source-only Control Center candidate. Their installed Windows launchers still share Docker and may require guarded bootstrap of shared tmpfs settings; cold-start independence has **not** been verified.
- Updater and watcher actions use Docker container `fortis-repo-test`.
- Updater checks `/workspace/repo/Workfiles/rdg-ci/ready.json` and
  `http://localhost:18880/healthz` before entering watch mode.
- Watcher start/stop calls the existing Fortis Guard functions from
  `Common.ps1`; it does not start the RDG updater.
- `CI Watcher — запуск` and `CI Watcher — остановка` are one-shot commands.
  The actual watcher is a Docker process, not a persistent Windows worker PID.

The importer displays whether each required Windows file exists at import
time. Docker/container health is deliberately checked only when the user
explicitly launches the corresponding worker.

## Git-clone source transition (2026-11-01)

For the new source-of-truth design, the worker files **in the Windows Git clone** now resolve the Fortis Repo 1/2 and shared Guard scripts under `launchers/` relative to their own `$PSScriptRoot`. The external installed-file table above documents the **historical pre-migration** paths; it is not the new runtime contract. The updater executable remains external to this repository. The Repo 1/2 native runtime, keys and profiles also remain machine-local. Refer to `docs/GIT-WINDOWS-SOURCE-OF-TRUTH.md` for acceptance and rollback. Do not retire old Windows launchers before a real reconnect test.
