# Script Control Center

Source for the **Windows Fortis / RDG Control Center** and its user-script catalog. The repository contains a **source-only candidate** imported from the Fortis Repo workspace on 2026-11-01; it is **not** evidence that the updated version is installed or verified on Windows.

## What it does

- `Start-ScriptCatalog.ps1` opens the independent script catalog UI (without starting the Fortis/RDG service cards).
- `Start-ControlCenter.ps1` opens the combined Fortis/RDG Control Center and catalog.
- `ScriptCatalog.cs` implements the script registration, status and log-view UI; `Script-Runner.ps1` runs supported registered commands in the configured mode.
- The `Repo*-Worker.ps1`, `Updater-Worker.ps1`, and `Watcher-*.ps1` files are **Windows adapters**, not portable launchers or permission to change working infrastructure.
- `BUNDLE-MANIFEST.md` documents the optional preset and external installed-file dependencies. Importing a preset is registration-only and does not launch the workers.

## Requirements and verification

The UI requires **Windows PowerShell 5.1, Windows Forms and an STA thread**. It is not a Linux-native application. The existing Docker workspace can edit the source and run the static Python regression tests, but it cannot verify real WinForms compilation or GUI behavior.

```bash
python3 -B test_static_catalog.py
```

On a **separate Windows test copy**, with explicit approval and without touching running Fortis/RDG sessions:

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
