# Script Center — отдельное включение machine PowerShell ASK V1

**Status: SOURCE REVIEW ONLY (2026-11-17).** This document does not authorize starting, stopping, restarting, or replacing the installed Windows Executor. Do not run a second port-10443 listener. Preserve original TLS/config and SAME CURRENT SQLite receipt DB.

## Explicit owner-only opt-in

The existing Script Center entry Fortis-Owner-PowerShell-Executor.ps1 runs a single non-admin process in «Своё окно». The pre-existing protected script-center-launch.json retains its original five TLS/secret/receipt *paths*, without a schema change.

With NO separate owner-protected machine-script-center.json, Script Center passes no machine flags: current owner-only launch is unchanged.

An owner may later provision the separate protected file at %LOCALAPPDATA%\Fortis\OwnerShell\machine-script-center.json. It contains only the following six keys, never secret contents:

| JSON key | Exact contract |
| --- | --- |
| schemaVersion | integer 1 |
| enabled | boolean true |
| mode | string ASK_ONLY, never ALLOW |
| executorHeadSha | exact SHA of owner-reviewed Executor commit; the pin remains valid across clean synchronized forward commits changing only root README.md or Markdown under docs/ |
| machineBearerFile | absolute path to protected machine-bearer file, direct child of OwnerShell |
| machineProofFile | absolute path to distinct proof-key file, direct child of OwnerShell |

Source fixes the machine actor as repo-sonar-read-proposal-actor and target as windows-owner. The machine, AI and JSON cannot override either. Source is pinned to the specified Executor SHA in the owner config. The
Script Center launcher first requires a clean, synchronized Windows main.
When HEAD advances beyond the pinned commit, the launcher verifies that the pin
is an ancestor and that **all paths except root README.md and Markdown files
under docs/** have unchanged Git content and file modes. Documentation-only
fast-forwards retain the same protected opt-in, existing credentials and receipt DB.
A changed executable, other source/config file, deleted/renamed code path,
non-ancestor pin or unavailable Git object remains a hard STOP.
After any such change the owner must independently review the accepted
Executor source and explicitly update the protected SHA pin using the normal
owner-controlled configuration procedure; there is no automatic pin rewrite.
GitHub fetch, Windows clone mutation and machine secret disclosure are forbidden
in the launcher.

The existing Script Center private-file validator checks the config NTFS owner, inheritance and ACL. The *Executor-owned* Assert-FortisMachineSecretFiles helper checks the two secret-file paths, protected directory, reparse points, ACLs and non-reuse of the old owner secret. Both machine keys must already exist under the protected directory. Failures STOP; there is no silent fallback to ALLOW or regeneration of keys. The new helper only reads *paths*, never key contents.

**DO NOT** paste machine bearer/proof-key contents into ChatGPT, Repo, GitHub or logs. Separate secure key provisioning, Windows ACL verification and protected Gateway mount /run/fortis-machine-powershell require owner review. No credentials are created by this source.

## Release/rollback gates, not done by this task

1. Review source PR and exact SHA Windows/Linux CI; owner merge and manual clean Windows Script Center clone update.
2. Separately approve new protected machine key/bearer files, matching owner-private Gateway ASK_ONLY mount, existing non-admin identity and target, and source SHA.
3. Owner checks there are no unresolved effects in the original receipt DB. Gracefully stop the ONE old owner-only Executor window (no force-kill or second listener), confirm port 10443 is clear, then start ONE registered Script Center instance with the accepted opt-in.
4. Deploy the reviewed Gateway image/mount through owner Check/Apply and owner-controlled rollout. No AI bypass or direct owner-only execute proxy.
5. Live acceptance with fresh UUID: Repo AI submit → Core ASK → real owner WebAuthn → exactly one Windows execution → bound result; Reject/forged-proof/timeout/lost-ACK/restart negatives are required.
6. Rollback: stop the new singleton first, disable separate opt-in through owner-controlled procedure, restart owner-only mode against the **same CURRENT** TLS/config/receipt DB. Never restore old DB snapshot or replay an uncertain command.

ASK is human confirmation, **not** a sandbox or automatic command safety assessment. Owner-editable restrictions and optional narrowly constrained ALLOW belong to V2+.
