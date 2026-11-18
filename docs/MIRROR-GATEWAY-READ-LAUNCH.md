# Windows Mirror GET-only HTTPS — opt-in Script Center

Owner-only convenience adapter. Mirror updates no longer require editing
SHA pins in Script Center. The adapter requires a **clean, synchronized**
Windows `docke-windows-mirror/main`: canonical primary clone, approved GitHub
origin and exact `HEAD == refs/remotes/origin/main`. The check is read-only,
works offline with the already-fetched Git refs, and does not start a network
Git update. The actual verified HEAD is passed to the Mirror-owned listener as
`-SourceSha`, where it is rechecked before binding.

This is a deliberate change from allowing only one fixed Mirror revision:
**future reviewed commits merged to owner GitHub `main` are trusted without a
new Script Center PR**. Source trust is delegated to that repository's merge
process, not to an independent review of each listener change. A clean synced
checkout is not proof that arbitrary future code is safe. Keep origin/main and
owner account protected. The GET-only implementation and its permissions must
still be enforced by the Mirror repository itself.

Use Script Center → «Импорт из текста» and paste the one non-comment line from
\`TEXT-IMPORT-MIRROR-GATEWAY-READ.txt\`. Mode must be \`window\` / «Своё окно».
Import registers the adapter, it **does not** configure or start an API.

First Start opens a small Windows form for **existing file paths only**:
public server certificate, owner-private server key, owner-private bearer file.
Files must be under LOCALAPPDATA, non-symlinks, with protected owner ACL for
the two private inputs. The adapter stores **only paths** in an ACL-protected
owner-local config and exits with MIRROR_HTTPS_STARTED=NO. It never creates
keys, bearer tokens, a CA, a Gateway alias, or a vault volume.

Next Start rechecks source/paths and starts only the canonical
\`docke-windows-mirror/Start-MirrorGatewayReadOnly.ps1\` with exact 127.0.0.1,
port 443, verified current source SHA, and fortis-approvals allowlist. Ctrl+C stops the
foreground listener. A busy port is UNKNOWN, not proof the correct API runs;
the adapter refuses to start a second listener. It never calls Browser Pull.

Optional \`-Status\` checks the accepted local source and protected paths.
For port 443, both Windows `Get-NetTCPConnection` and a bounded loopback TCP
connect to `127.0.0.1:443` are best-effort observations: Windows enumeration
can miss an active Docker-reachable listener. Either observation of occupancy
reports `MIRROR_HTTPS_PORT_443=OCCUPIED_OR_UNKNOWN`; otherwise it reports
`MIRROR_HTTPS_PORT_443=NOT_CONFIRMED` — **not** proof that the port is free
or that Mirror has stopped. The actual listener bind is authoritative for start.
These checks never submit a Fortis Core request, perform TLS/Bearer verification,
create credentials, or start the listener. For owner release readiness, verify
TLS/CA/hostname and a scoped authenticated GET from the actual Gateway
container with no new Core effect after sealed Check (UC-GW-012 and Mirror
runbook). This adapter does not install services, automatically survive
reboot, or overwrite configured paths; path changes need owner review.
Windows PowerShell 5.1/WinForms/owner ACL live acceptance is still pending.
