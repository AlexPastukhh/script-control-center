# Windows Mirror GET-only HTTPS — opt-in Script Center

Owner-only convenience adapter. Requires an independently accepted, clean Windows
docke-windows-mirror main, pinned here to \`efa353791e1cd79ef5d0122019ffb982e21cab4e\`.
Newer Mirror source requires a reviewed source-pin update, never bypass.

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
port 443, source SHA, and fortis-approvals allowlist. Ctrl+C stops the
foreground listener. A busy port is UNKNOWN, not proof the correct API runs;
the adapter refuses to start a second listener. It never calls Browser Pull.

Optional \`-Status\` checks local source, paths and port without starting.
It does **not** perform TLS, bearer or Gateway-to-Windows live health checks.
Those belong to UC-GW-012 and the canonical Mirror runbook. This adapter does
not install services or automatically survive reboot. Any change of configured
paths requires separate owner review; existing config is never overwritten.
Windows PowerShell 5.1/WinForms/owner ACL live acceptance is still pending.
