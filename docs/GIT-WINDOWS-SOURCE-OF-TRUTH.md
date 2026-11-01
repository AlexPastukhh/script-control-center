# Git as Windows launcher source of truth (transition 2026-11-01)

## Directory mapping

The Windows **mirror** is the per-repository Git checkout under `%USERPROFILE%\Repos\<repo>`, matching the naming convention of Docker `/workspace/repo/Repos/<repo>` (or `/workspace/repos/<repo>`). **They are not the same physical directory.** Transfer happens only by GitHub commit/push/pull, not by a Docker Windows bind mount or automatic filesystem mirroring. For this app the canonical Windows checkout is `%USERPROFILE%\Repos\script-control-center`; do not create a competing `%LOCALAPPDATA%` source copy. Windows `Repos`/`repos` casing is equivalent. Git does not transfer uncommitted changes.

## Intended path and responsibility

1. Author source inside the **existing** Fortis Repo Docker Git checkout (`/workspace/repos/script-control-center`).
2. Inspect/test code, review the diff and any pinned resources, `git commit`, then `git push` (current owner policy permits `main`).
3. On Windows, `git pull --ff-only origin main` into a **separate Windows Git clone** (default `%USERPROFILE%\Repos\script-control-center`). Git is the transfer mechanism; there is no shared Windows bind mount.
4. The single Desktop `Fortis RDG Control Center.lnk` launches **`Start-ScriptCatalog.ps1`** in that Windows clone. `Start-ControlCenter.ps1` is the optional older combined four-card shell, **not** the primary shortcut. The catalog, worker adapters and Repo 1/2 resource guards live in Git there. Source code and worker scripts are not copied to `%LOCALAPPDATA%` during an update.
5. Installing/pulling source does not run arbitrary scripts or authorize an existing service restart. The owner explicitly chooses any worker Run; do not automatically stop or replace an active tunnel.

Use `tools/Update-Windows-ControlCenter.ps1` **from the Windows clone** when explicit source preflight and shortcut repair are needed, **not for routine Git pull**. Default is read-only. The optional `-Apply` pulls `main` fast-forward-only, runs two compile-only Windows WinForms tests and both Repo 1/2 read-only Previews, then points the Desktop shortcut at `Start-ScriptCatalog.ps1` after all checks pass. It does not register new scripts, start a tunnel, change Docker, update pins, or run a worker. WinForms compilation and live connection still require separate owner testing. For initial clone, use `git clone https://github.com/AlexPastukhh/script-control-center.git "$env:USERPROFILE\Repos\script-control-center"` in Windows PowerShell.

## Location and scope

- Git source: **`Start-ScriptCatalog.ps1`** (primary standalone catalog), `ScriptCatalog.cs`, root `*-Worker.ps1`; optional `Start-ControlCenter.ps1` is the older combined shell.
- Repo 1 code: `launchers/chatgpt-repo-mode/{run-tunnel-quiet-logs.ps1,run-tunnel.ps1,control.ps1}`. `control.ps1` is kept in Git, with a new checked SHA-256 pin in both Repo 1 launchers; `workspace-config.json`, runtime/profiles remain Windows-local. Legacy `probe.mjs` is not used by the quiet launcher and was not in the export snapshot: collect fresh Windows bytes before including it.
- Repo 2 code: `launchers/FortisRepoPlus2/{Repo2DirectGuard.ps1,FortisRepo2-quiet-logs.ps1}`.
- Shared Guard: `launchers/FortisRDGLauncherV2/{Common.ps1,Start-Fortis.ps1,Guard-Once.ps1}`. The rest of the old V2 tools are **not yet all migrated** and may still be invoked from installed Windows folders via old shortcuts/tasks.
- Repo 2 profile, tunnel key and native runtime remain Windows-local. Repo 1 local profiles and runtime logs remain Windows-local. Never copy them into Git.
- RDG updater itself is still an external RDG Windows dependency and is **not** migrated in this change. Its long-term ownership is RDG; migrating it requires a separate tested release process.
- Local catalog `catalog.xml` and logs remain under `%LOCALAPPDATA%\FortisRdgControlCenter\scripts`, not in source control. The catalog stores **absolute paths**. On this Windows machine the existing five bundled entries have already been re-pointed to the Git clone, and catalog/shortcut backups were created; **do not reimport on each pull**. On a fresh Windows machine open the standalone catalog directly and use «Готовый набор» once. Its dependency preview still lists some *historical* `%LOCALAPPDATA%` paths; those are not the new Repo 1/2 launcher paths.

## Docker resource changes: preserve the recovery connection

**Before stopping the currently connected Fortis Repo** after a Docker image, container ID, resources, mounts, network or isolation policy change:

1. Keep the existing Repo 1/2 session alive where possible; a currently running connection does not prove a later reconnect will work. If a container recreation itself kills MCP, prepare compatible startup source and a recovery procedure *before* recreation.
2. Run Docker `inspect` read-only. Compare both Repo launchers and shared Guard in `script-control-center`, especially pinned image ID/container ID, memory, CPU, PIDs, tmpfs, mounts, network, UID and fingerprints. Do not weaken unrelated checks.
3. Change the **canonical Git source**, update pinned SHA-256 only for a verified reviewed content change. Run static tests. For this transition memory is allowed in **[4, 8] GiB** and PIDs in **[256, 1024]** for startup scripts; CPU and other constraints remain pinned.
4. Commit/push the reviewed changes. Windows pulls a specific fast-forward revision into the cloned source and checks it. No direct copy from Docker snapshots to installed Windows scripts.
5. Test *read-only* `Repo1 ... -Mode Preview` and `Repo2DirectGuard.ps1 -Mode Preview` on Windows, and verify the Guard hash against the launcher. Do not launch duplicate tunnels, stop a working process, invoke `-Mode Run` during verification, or switch the main shortcut before the candidate passes.
6. Test real reconnection in a **deliberate owner-controlled window** after an alternate working channel/recovery path is available. Only then declare cold start accepted or retire old installed scripts/shortcuts. `Preview=PASS` by itself does not prove a new tunnel connection.

## Verified source provenance / limitations

- The dated `windows-installed-sources/20261101-085708-2d7ce467` snapshot contains the older installed source; it is a historical input, **not** current Windows state and is not committed as a runtime bundle.
- `Repo2DirectGuard.ps1` was reconstructed with only the approved memory-range modification, and the resulting SHA-256 is **08AB2A7B3051E6899A47AF8CD09FC2FFE6E560CD7638B8160D1B2C3A40402241**, equal to the owner-reported Windows SHA. The Repo 2 quiet launcher pins this exact SHA. `launchers/**/*.ps1 -text` prevents a different Git autocrlf setting from silently changing the guard bytes.
- Repo 1 `run-tunnel-quiet-logs.ps1` was reconstructed byte-for-byte to the owner-reported amended SHA **28446727513ABE85136F886D2844C2C39C544FB497E76A31BEE9DFB922D99916** *before* changing its control-script source path to the Git clone. That additional path change is not yet live-tested on Windows.
- Repo 1 `run-tunnel.ps1` also gets a legacy exact-256 PID check widened to 256–1024 in the candidate, in addition to 4–8 GiB memory and same-clone control path. It is not the currently used quiet launcher.
- The cloned Repo 1 `control.ps1` pin was updated in source after changing its workspace-config lookup to the original Windows-local location. The owner confirmed that the standalone catalog opens from the Windows Git clone and displays the registered workers; this verifies the **UI only**. The installed Repo 1 previously reported `Preview=PASS`, and its existing MCP connection was verified. **Repo 2 and the cloned-source launchers are not yet end-to-end reconnect-accepted.** No Windows services or live tunnels were restarted to test these launchers.
- Run tests in Docker: `python3 -B test_static_catalog.py` and `python3 -B test_git_launcher_contract.py`. On Windows, separately run the source-only WinForms compile test and the read-only Guard/Repo1 Preview before considering cutover.

## Rollback

- Keep Desktop `.lnk` backups created during the owner's first setup and later catalog-only correction (`*.before-git-*.bak`, `*.before-catalog-*.bak`) and the original installed Windows launchers until the repo-clone workers pass reconnection acceptance.
- Git `pull --ff-only` never overwrites a dirty Windows checkout; do not use `reset --hard`, `git clean`, force-push, or change pins to make a mismatched Guard pass.
- A Git rollback changes source, not existing running processes. Use a reviewed previous commit or restore the Desktop `.lnk` backup in an owner-controlled action if necessary.
