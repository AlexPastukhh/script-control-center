# Owner PowerShell Executor — opt-in Script Center entry

**Migration source candidate:** this adapter now targets the standalone
`fortis-powershell-executor` repository. Install only after its bootstrap,
accepted main and Windows clone are ready. Current installed processes are
not switched by this source change; there is no fallback to Mirror.
Existing catalog registration and the five saved TLS/receipt paths stay valid.


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
automatically, then the accepted Executor-owned
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

The adapter requires a non-admin Windows identity and a trusted, **clean,
synchronized** Windows `fortis-powershell-executor/main` clone. On each launch it
reads Git metadata without fetching or modifying source and verifies:

- the canonical `%USERPROFILE%\Repos\fortis-powershell-executor` primary checkout,
  with a real `.git` directory (not a symlink or task worktree);
- the owner GitHub `AlexPastukhh/fortis-powershell-executor` origin;
- branch `main`, clean tracked/untracked state, and **HEAD exactly equals the
  locally fetched `refs/remotes/origin/main`**.

This replaces the historical fixed SHA and source-tree pins that blocked
launch after unrelated Browser updates. An approved Executor `main` update
arriving through the normal Windows clone sync needs **no subsequent Script
Center source-pin edit or additional PR**. The adapter does not fetch or
self-update at launch and cannot independently check whether the actual
GitHub remote advanced since the last fetch; the normal approved Windows
update remains responsible for delivering the latest source.

The Owner Shell's executable code is now trusted as part of that clean
owner-controlled GitHub `main`, rather than only at one fixed commit. That is
an intentional trust-boundary change: future merged `owner_shell.py` changes
can launch automatically. Protect GitHub merges and the Windows account;
Git cleanliness alone does not prove that the code itself is safe.

The adapter still checks the protected key, Bearer, config and existing receipt
DB ACL, an unoccupied port 10443, and binds only to 127.0.0.1:10443. It does
no Git update, Docker change, Gateway release, service installation or firewall
change.

A new empty receipt DB breaks durable UUID replay and UNCERTAIN
protection: never substitute one. This service grants authenticated
Owner Portal commands arbitrary Windows user-level execution and
is NOT a sandbox. Previous owner TLS material, identity, receipt DB
and Portal session/Origin/CSRF checks require separate owner review.
Windows PowerShell 5.1 live acceptance is not covered by source tests.


## Machine ASK V1 — separate owner-controlled opt-in

The existing five-path protected Script Center launch file and original
owner-only behavior remain unchanged. A separate owner-created protected
machine-script-center.json must explicitly request ASK_ONLY V1. Absent this
file, the launcher passes no new machine flags. It validates the approved
Executor SHA and the two new machine-secret paths but never creates or
prints credentials. See MACHINE-POWERSHELL-ASK-OPTIN-V1.md for the exact
schema and gated single-listener rollout procedure. Source merge alone
does not activate a machine API.
