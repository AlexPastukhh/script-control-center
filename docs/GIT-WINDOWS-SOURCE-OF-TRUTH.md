# Script Center: Windows clone and paired update

This document owns the Windows-side procedure for updating the Script Catalog and its Repo1/Repo2 adapters. The actual worker/Guard source and Docker compatibility rules are owned by `tunnel`; see the [Tunnel Windows Repo runbook](https://github.com/AlexPastukhh/tunnel/blob/main/runtime/windows-repo/README.md) and [Guard-change checklist](https://github.com/AlexPastukhh/tunnel/blob/main/docs/FORTIS_GIT_WINDOWS_LAUNCHER_WORKFLOW.md).

## Ownership

- `tunnel/main` owns the Repo1/Repo2 workers, launcher chain and shared `FortisContainerPolicy.ps1`.
- `script-control-center/main` owns the Script Catalog/Control Center UI, five registrations, thin worker adapters, resolver and paired updater.
- Windows profiles, keys, ACLs, Tunnel Client binaries, Docker state, logs and catalog runtime data remain Windows-local; never copy them into Git.

## Canonical paths

| Role | Docker source | Windows clone |
|---|---|---|
| Script Center UI/adapters | `/workspace/repo/Repos/script-control-center` | `%USERPROFILE%\Repos\script-control-center` |
| Repo1/Repo2 workers/Guards | `tunnel/runtime/windows-repo/current` | `%USERPROFILE%\Repos\tunnel\runtime\windows-repo\current` |

These are separate Git checkouts. GitHub is the transfer channel; there is no automatic filesystem sync or shared writable Windows bind mount. Pull only committed source.

## Shortcut and registrations

The Desktop `Fortis RDG Control Center.lnk` targets `Start-ScriptCatalog.ps1` in the Script Center Windows clone. `Start-ControlCenter.ps1` is the optional older four-card shell, not the primary shortcut. The five catalog registrations retain paths into the stable Script Center clone; do not reimport them after routine pulls.

## First-time clone

Run only if a clone is missing; never overwrite an existing directory. Use ordinary, non-admin Windows PowerShell:
```powershell
$repos = Join-Path $env:USERPROFILE 'Repos'
New-Item -ItemType Directory -Path $repos -Force | Out-Null
foreach ($name in @('tunnel','script-control-center')) {
    $path = Join-Path $repos $name
    if (Test-Path -LiteralPath $path) {
        if (-not (Test-Path -LiteralPath (Join-Path $path '.git'))) { throw "Not a Git clone: $path" }
        git -C $path status --short --branch
        git -C $path remote get-url origin
    } else {
        git clone --branch main --single-branch "https://github.com/AlexPastukhh/$name.git" $path
        if ($LASTEXITCODE -ne 0) { throw "Clone failed: $name" }
    }
}
```

If a target already exists, inspect its `.git`, origin, branch and `git status` first. Do not delete or replace it to make cloning succeed.

## Routine paired update

From the Script Center Windows clone, first run the read-only Preview:

```powershell
.\tools\Update-Windows-ControlCenter.ps1
```

After reviewing a PASS, explicitly apply the paired fast-forward:

```powershell
.\tools\Update-Windows-ControlCenter.ps1 -Apply
```

`-Apply` checks both clean, synchronized `main` clones and approved origins; pulls `tunnel` first and Script Center second; verifies the nine-file manifest and hashes; parses PowerShell; compiles the WinForms sources; runs read-only Repo1/Repo2 Previews from the Windows Tunnel clone; then backs up/repairs the existing shortcut. It does not import registrations, start/stop a worker, restart Tunnel Client, or stop/recreate Docker.

## GO3 release verifier stored in the Docker workspace

`_go3-revision07/Invoke-GO3-Guard-Previews.ps1` is currently an uncommitted Docker-workspace file, not part of the Windows Git clone. It is intentionally executed on Windows because it inspects Docker Desktop and invokes the Windows Tunnel Guard launchers. Do not try to run it from `%USERPROFILE%\Repos\script-control-center` until it has been committed and pulled.

For the current baseline, copy the exact verifier out of the running Repo container into `%TEMP%`, then execute it in ordinary, non-elevated PowerShell:

```powershell
$containerId = '04fed7b726a8536d63c4d2533cfc7ee3072956ff0c3f571504d7f709f7909030'
$dockerPath = '/workspace/repos/script-control-center/_go3-revision07/Invoke-GO3-Guard-Previews.ps1'
$tempScript = Join-Path $env:TEMP 'Invoke-GO3-Guard-Previews.ps1'
docker cp "${containerId}:$dockerPath" $tempScript
if ($LASTEXITCODE -ne 0) { throw 'Docker copy failed' }
& $tempScript
```

The verifier is read-only: it pins the current container/image and canonical Tunnel manifest, then runs Repo1 and Repo2 Guard Preview. It does not start/stop tunnels, edit Windows profiles, repair Docker configuration or issue Sonar requests. Its current pins deliberately reject a different container/image until the source is reviewed and updated. The owner completed the corrected Windows run on 2026-11-04; it emitted `GO3_RELEASE_VERIFICATION_PASS` and validated the four volume name/destination/RW entries. The first run's malformed mount subsection is superseded and must not be reused. Actual Repo1/Repo2 reconnect and functional acceptance remain a separate pending gate.

This `docker cp` workflow is an ephemeral execution bridge, not a source-sync method. Do not copy it into the Windows Git clone or commit Windows-local runtime output.

## Reconnect and rollback

A successful pull, hash check, compile or Preview is not a live MCP reconnect test. If a reconnect is required, use a separate owner-controlled window, reconnect one Repo at a time, keep the other working route and recovery path available, and verify real MCP responses. Do not stop the shared Docker container as part of a routine source update.

If a clone is dirty, on the wrong branch or diverged, the updater stops. Preserve and review local changes; never use `reset --hard`, `git clean`, stash or force-push to bypass the gate. Keep shortcut/launcher backups until the updated routes are accepted. Current owner-reported reconnect status and remaining cold-boot limits are recorded in the Tunnel runbook.
