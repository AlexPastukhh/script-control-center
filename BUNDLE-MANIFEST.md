# Fortis / RDG bundle manifest

## Worker files in the Windows Script Center clone

The catalog importer resolves these five adapter files relative to the folder
containing `Start-ScriptCatalog.ps1`:

| Display name | Worker file | Role |
|---|---|---|
| Fortis Repo 2 | `Repo2-Worker.ps1` | Delegate to the pinned Repo 2 worker in the tunnel clone |
| Fortis Repo 1 | `Repo1-Worker.ps1` | Delegate to the pinned Repo 1 worker in the tunnel clone |
| RDG Updater | `Updater-Worker.ps1` | Prepare Fortis from tunnel source, then watch an already-verified RDG build |
| CI Watcher — запуск | `Watcher-Start.ps1` | Use tunnel-owned shared Fortis code and ensure the Docker CI watcher |
| CI Watcher — остановка | `Watcher-Stop.ps1` | Use tunnel-owned shared Fortis code and stop only the matched CI watcher |

All five are registered in hidden/background mode with no arguments.
Import only registers file paths; it never runs a worker or contacts Docker.
Stable bundle IDs are stored in the catalog to prevent repeated imports.

## Authoritative Windows tunnel source

The five Script Center adapters use `Get-TunnelWindowsMirror.ps1`. It accepts
only the canonical clean `main` clones with their approved GitHub origins and
fetched `origin/main` in sync:

- Script Center: `%USERPROFILE%\Repos\script-control-center`
- Tunnel: `%USERPROFILE%\Repos\tunnel`
- Worker/launcher bundle: `%USERPROFILE%\Repos\tunnel\runtime\windows-repo\current`

The resolver verifies the pinned `source-manifest.json` fingerprint and all
eight SHA-256 file hashes before it returns a source path. Missing, dirty,
stale, wrong-branch or modified source fails closed. It never falls back to
`script-control-center\launchers`, `%LOCALAPPDATA%` launcher copies, Docker
paths or another arbitrary Windows directory.

| Worker | Files loaded from the tunnel clone |
|---|---|
| Repo 1 | `Repo1-Worker.ps1`, `launchers\chatgpt-repo-mode\run-tunnel-quiet-logs.ps1`, adjacent `control.ps1` |
| Repo 2 | `Repo2-Worker.ps1`, `launchers\FortisRDGLauncherV2\Start-Fortis.ps1`, `Common.ps1`, `launchers\FortisRepoPlus2\Repo2DirectGuard.ps1`, `FortisRepo2-quiet-logs.ps1` |
| RDG Updater | `Start-Fortis.ps1` and `Common.ps1` |
| CI Watcher start/stop | `Common.ps1` and the adjacent Repo 2 Guard |

The old `script-control-center\launchers` files are retained for historical
source/contract tests only. They are not used by Script Center worker execution.

## Windows-local runtime and state dependencies

These are deliberately **not source files** and must not be copied into Git:

- Windows Tunnel Client: `C:\Tools\tunnel-client\tunnel-client.exe` and
  `C:\Tools\tunnel-client\runtime\v0.0.15\tunnel-client-runtime.exe`.
- Repo 1 local config/profile/log state under the existing
  `%USERPROFILE%\chatgpt-repo-mode` folder.
- Repo 2 profile, separate key, ACLs and logs under
  `%LOCALAPPDATA%\FortisRepoPlus2`.
- RDG updater executable at
  `%LOCALAPPDATA%\RDG-Server\cicd-bootstrap-ci-20261029t051843803595-4546c7d69e\files\RDG-CICD-Bootstrap\Update-RDG.ps1`.
- Docker CLI, existing `fortis-repo-test` container/image/volume/config, local
  catalog XML and logs under `%LOCALAPPDATA%\FortisRdgControlCenter\scripts`.

The bundle import preview checks the tunnel source files, resolver, known
Windows runtime binaries and required local state paths using existence checks
only. It does not read secret contents, execute the resolver, access Docker or
start a worker. Actual launch performs the full Git/manifest/hash preflight and
then the existing per-launcher Guards.

## Runtime requirements not exercised by import

- Repo 1 and Repo 2 are independently selectable. Neither Script Center adapter
  requires the other tunnel process to be running; shared Docker/DC/bootstrap
  constraints are still enforced by the existing Guards. Cold-start behavior
  remains unverified until a real Windows reconnect is accepted.
- Repo 2 `Start-Fortis.ps1` may start the existing pinned Docker container if
  it is stopped and may run its recognized guarded repair. It does not create,
  recreate or stop the container.
- Updater checks `/workspace/repo/Workfiles/rdg-ci/ready.json` and
  `http://localhost:18880/healthz` before entering watch mode.
- Watcher start/stop calls the existing Fortis Guard functions from
  `Common.ps1`; it does not start the RDG updater. The actual watcher is a
  Docker process, not a persistent Windows worker PID.

## Paired Git-clone update and acceptance

Push reviewed changes to both Docker repositories: `tunnel` first, then
`script-control-center`. On Windows pull both clean `main` clones in that order,
or run `tools\Update-Windows-ControlCenter.ps1 -Apply` from the Script Center
clone. That tool validates the mirror, parses PowerShell, compiles the WinForms
sources, runs read-only Repo 1/2 Previews from the tunnel clone and repairs the
existing Script Catalog shortcut. It does not start workers or stop Docker.

Do not reimport the five entries after routine pulls. After both read-only
Previews pass, restart only one Windows tunnel profile at a time from Script
Center, keep the other tunnel and an independent recovery path alive, and
confirm real MCP responses before switching the second profile. Do **not** stop
or recreate `fortis-repo-test` as part of this source cutover.
