# Owner PowerShell Executor — opt-in Script Center entry

Register Fortis-Owner-PowerShell-Executor.ps1 through
«Импорт из текста» using the one line in
TEXT-IMPORT-OWNER-POWERSHELL.txt, then «Проверить» →
«Импортировать». Mode must be window / «Своё окно».
Import does not start the executor or configure Gateway.

On first Start, a **visible WinForms setup dialog** opens in the catalog's
separate PowerShell window. It has five file-path fields and file-browser
buttons for the already provisioned Windows server certificate, protected
server key, Portal client CA, Bearer SECRET FILE (not the bearer contents),
and SAME EXISTING SQLite receipt DB. Known local filenames are suggested
when they exist; the owner must check the choices before saving. No secret
contents are requested or displayed. **Cancel** writes nothing.

**Проверить и сохранить** validates all five existing files first, including
LOCALAPPDATA containment, no reparse points, protected ACL on private files,
and an existing SQLite header for the receipt DB. Errors appear in a dialog
without closing the setup form; in particular, `OWNER_SHELL_ACL_serverKey`
means the key ACL requires separate owner review, not disabling checks.
On success it writes only the five absolute paths to the ACL-protected
`%LOCALAPPDATA%\Fortis\OwnerShell\script-center-launch.json` and prints
`OWNER_SHELL_PATHS_CONFIGURED=YES`, `OWNER_SHELL_STARTED=NO`. No Python
service is launched by the save operation.

Click Start again: the existing protected paths are loaded and validated
automatically, then the accepted Mirror-owned
`Start-OwnerPowerShellExecutor.ps1` runs. No repeated setup or file prompts.
Ctrl+C stops the running Executor in its console. Fatal preflight errors
also show a modal error dialog instead of disappearing with a closed console.

To re-open the same setup UI intentionally, run the catalog adapter with
`-Configure` in its own window (for example, temporarily set the registered
entry's Arguments to `-Configure`, then restore the empty Arguments after
setup). Existing paths are prefilled and the config is atomically replaced
with protected ACL. The saved receipt DB identity **cannot be changed** through
this convenience UI: a different DB needs an independently reviewed
owner recovery procedure. Do not create an empty DB or replace secrets here.

The adapter requires a non-admin Windows identity and the **exact clean**
`docke-windows-mirror/main` commit
`c0f5db887f29996ee3464cf991674c31d0a22eaf`, descended from the
accepted base `a5cb3e9f61e68331c11337eb5da4af056d27660c`.

Mirror PR #19–22 modified Browser and `mirror_api/operations.py`, so the old
"only docs/GATEWAY-READ-ONLY.md changed since base" restriction rejects the
current clean Mirror source. In the reviewed current commit the Owner Shell
execution code is unchanged: `Start-OwnerPowerShellExecutor.ps1`,
`mirror_api/owner_shell.py` and package `mirror_api/__init__.py` still have the
previous exact Git blob IDs. The Owner Shell Python module imports only the
standard library, not `mirror_api/operations.py`. The adapter therefore
requires **both** the exact accepted Mirror commit and those immutable runtime
blob IDs; a newer Mirror commit fails closed with
`OWNER_SHELL_SOURCE_PIN_CHANGED`, rather than silently accepting new source.
No broad `mirror_api` tree allowlist or generic SHA bypass is used.

The adapter still checks Git origin, clean main, protected key, Bearer, config
and existing receipt DB ACL; unoccupied port 10443; and only binds to
127.0.0.1:10443. It does no Git update, Docker change, Gateway release,
service installation or firewall change. A future Mirror update requires a
separate explicit source review and pin update.

A new empty receipt DB breaks durable UUID replay and UNCERTAIN
protection: never substitute one. This service grants authenticated
Owner Portal commands arbitrary Windows user-level execution and
is NOT a sandbox. Previous owner TLS material, identity, receipt DB
and Portal session/Origin/CSRF checks require separate owner review.
Windows PowerShell 5.1 live acceptance is not covered by source tests.
