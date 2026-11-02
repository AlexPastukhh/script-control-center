# Git as Windows launcher source of truth (paired release, 2026-11-02)

## Directory mapping

Docker and Windows have **separate Git checkouts**. GitHub is the only transfer mechanism; there is no shared Windows bind mount or automatic filesystem mirroring.

| Role | Docker | Windows |
| --- | --- | --- |
| Script Center UI + thin worker adapters | `/workspace/repos/script-control-center` | `%USERPROFILE%\Repos\script-control-center` |
| Repo1/Repo2 worker + launcher source | `/workspace/repos/tunnel/runtime/windows-repo/current` | `%USERPROFILE%\Repos\tunnel\runtime\windows-repo\current` |

The Windows clones must be siblings under the existing `%USERPROFILE%\Repos` directory. Do not use `%LOCALAPPDATA%` as a source checkout, do not use a second `repos` folder, and do not run Windows PowerShell scripts from Docker paths.

## Execution ownership and source resolution

1. `Start-ScriptCatalog.ps1` is the primary Desktop shortcut target. The five catalog registrations retain their absolute paths into the stable `script-control-center` clone; do not reimport them after pulls.
2. `Repo1-Worker.ps1` and `Repo2-Worker.ps1` in Script Center are **thin adapters**. They dot-source `Get-TunnelWindowsMirror.ps1` and delegate to `%USERPROFILE%\Repos\tunnel\runtime\windows-repo\current\Repo1-Worker.ps1` / `Repo2-Worker.ps1`.
3. The resolver accepts only the canonical clean `main` Script Center clone and its exact sibling Windows `tunnel` Git clone with approved origins, clean `main`, the pinned `source-manifest.json` fingerprint, and all nine matching SHA-256 file hashes, including the shared `launchers/shared/FortisContainerPolicy.ps1` module used by both Repo1 and Repo2. Missing, dirty, stale, modified or wrong-branch source is a hard failure. There is no fallback to `script-control-center\launchers`, `%LOCALAPPDATA%`, Docker paths or other arbitrary Windows directories.
4. `Updater-Worker.ps1`, `Watcher-Start.ps1` and `Watcher-Stop.ps1` resolve shared `Start-Fortis.ps1` / `Common.ps1` from the same verified tunnel mirror. The RDG updater script itself remains an external Windows-local RDG dependency and is checked explicitly.
5. The `script-control-center\launchers` tree is retained for historical source/contract tests only; Script Center worker execution no longer uses it.

Windows-local Tunnel Client binaries, Docker CLI, profiles, keys, ACLs, logs, catalog data and the external RDG updater remain Windows-local by design. They are runtime/state dependencies, not source launchers. Never copy credentials, profiles or logs into Git.

## Paired Docker → GitHub → Windows update

Author in the two existing Docker Git checkouts. Review and test both changes, then commit/push only the intended files in each repository. The tunnel repo owns the complete Repo1/Repo2 Windows mirror; Script Center owns the UI and adapters that consume that mirror.

On Windows, run from the normal, non-admin PowerShell session in the Script Center clone:

```powershell
.\tools\Update-Windows-ControlCenter.ps1
```

This is read-only Preview. It verifies both sibling clones are on clean `main` branches with the expected GitHub origins and checks the tunnel mirror fingerprint and all nine file hashes. It does not pull, change the shortcut, start workers or change Docker.

After reviewing Preview, explicitly run:

```powershell
.\tools\Update-Windows-ControlCenter.ps1 -Apply
```

`-Apply` rechecks both clean clones, fast-forwards `tunnel` first and `script-control-center` second, verifies the exact tunnel manifest and every mirrored file, verifies the Repo2 Guard pin, parses active PowerShell sources, compiles both WinForms source sets in compile-only processes, runs Repo1 and Repo2 read-only Previews from the **Windows tunnel clone**, then backs up/repairs the existing Desktop shortcut to `Start-ScriptCatalog.ps1`. It does not register/import workers, run a worker, stop/start a tunnel, stop/start Docker, or read secret contents. If a pull or preflight fails, it stops; it never resets, stashes, cleans or force-checks out a clone.

If either Windows clone is missing, create it explicitly in Windows PowerShell:

```powershell
$repos = Join-Path $env:USERPROFILE 'Repos'
New-Item -ItemType Directory -Path $repos -Force | Out-Null
git clone --branch main --single-branch 'https://github.com/AlexPastukhh/tunnel.git' (Join-Path $repos 'tunnel')
git clone --branch main --single-branch 'https://github.com/AlexPastukhh/script-control-center.git' (Join-Path $repos 'script-control-center')
```

Do not reimport the five catalog entries after routine pulls. Their paths remain in the stable Script Center clone. Pulling source does not replace already-running processes.

## Controlled tunnel restart and acceptance

A successful Git pull, manifest check, PowerShell parse, WinForms compile or `Preview=PASS` is **not** a successful MCP reconnect. Keep the current working connection and recovery route until both previews pass and an owner-controlled maintenance window is ready.

- Do **not** stop/recreate the `fortis-repo-test` Docker container as part of this source cutover. The tunnel scripts expect the existing pinned container/image/mounts/resources; Repo2's `Start-Fortis.ps1` may start that existing container if stopped, but it does not recreate or stop it.
- Restart only one Windows tunnel profile at a time, from Script Center. Keep the other working profile and an independent recovery path available.
- First stop only the existing Repo1 worker/runtime using its existing Script Center Stop action; confirm it is stopped, then Run Repo1. Confirm real MCP reads and response delivery.
- Only after Repo1 is proven, stop only Repo2 and Run Repo2. Confirm its independent profile/browser route and real MCP response. Do not start a duplicate over a detected runtime/profile.
- If a preview fails, do not stop anything. If a reconnect fails, stop only the failed new instance and use the documented independent recovery path. Preserve old launcher/shortcut backups until both connections are accepted.

A full Docker-container stop would interrupt both tunnel routes and is **not** the procedure implemented by these launchers. Treat any container stop/recreation as a separate maintenance operation with its own backup and recovery plan.

## Verification and known limits

Docker offline checks:

```bash
python3 -B test_static_catalog.py
python3 -B test_git_launcher_contract.py
```

The tunnel mirror has its own `runtime/windows-repo/test_release_contract.py`. Windows `-Apply` runs the real Windows PowerShell parser and WinForms compile-only tests. This Docker environment cannot execute the Windows tunnel client, verify Windows ACLs, run the real GUI, or prove live Repo1/Repo2 reconnect. Those remain explicit Windows acceptance steps.

The installed Tunnel Client/Runtime executables under `C:\Tools\tunnel-client\...`, Windows profiles/keys, local logs, Docker container state and RDG updater remain outside Git. The exact paths and pins are documented in `tunnel/runtime/windows-repo/README.md` and its dependency inventory. Do not move them into the source mirror or substitute binaries from Docker.

## Rollback

- Keep the original Desktop shortcut and launcher backups until both tunnel routes pass real reconnect.
- `git pull --ff-only` never overwrites a dirty clone; do not use `reset --hard`, `git clean`, stash or force-push to bypass a mismatch.
- Reverting source changes does not terminate or replace an already-running process. Restore the previous reviewed commit or shortcut only through an owner-controlled action.
