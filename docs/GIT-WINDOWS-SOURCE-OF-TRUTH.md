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

This heading preserves the old link. The GO3 scripts are now tracked source,
including [`_go3-revision07/Invoke-GO3-Guard-Previews.ps1`](../_go3-revision07/Invoke-GO3-Guard-Previews.ps1).
The former copy/run command pinned container `04fed7b7…` from a dated baseline;
it is not a routine command for the current runtime. Do not restore an old
container, edit its pins blindly or run a historical cutover helper to make
Preview pass. Runtime/source provenance belongs to the
[container evidence](https://github.com/AlexPastukhh/fortis-container/blob/main/docs/audits/REPO-CONTAINER-GUARD-BASELINE-2026-11-04.md).

For routine source/Guard verification use the paired updater above and current
Tunnel source. The old corrected owner result remains dated evidence; a source
pull or Preview is separate from actual reconnect. Any future helper refresh
is tracked only in [BACKLOG-IMPROVEMENTS](BACKLOG-IMPROVEMENTS.md).

## Reconnect and rollback

A successful pull, hash check, compile or Preview is not a live MCP reconnect test. If a reconnect is required, use a separate owner-controlled window, reconnect one Repo at a time, keep the other working route and recovery path available, and verify real MCP responses. Do not stop the shared Docker container as part of a routine source update.

If a clone is dirty, on the wrong branch or diverged, the updater stops. Preserve and review local changes; never use `reset --hard`, `git clean`, stash or force-push to bypass the gate. Keep shortcut/launcher backups until the updated routes are accepted. Current owner-reported reconnect status and remaining cold-boot limits are recorded in the Tunnel runbook.
