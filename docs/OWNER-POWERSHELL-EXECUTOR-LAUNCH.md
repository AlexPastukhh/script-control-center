# Owner PowerShell Executor — opt-in Script Center entry

Register Fortis-Owner-PowerShell-Executor.ps1 through
«Импорт из текста» using the one line in
TEXT-IMPORT-OWNER-POWERSHELL.txt, then «Проверить» →
«Импортировать». Mode must be window / «Своё окно».
Import does not start the executor or configure Gateway.

On first Start, enter absolute paths ONLY to the already provisioned
Windows server certificate, protected server key, Portal client CA,
Bearer SECRET FILE (not the bearer contents), and SAME EXISTING SQLite
receipt DB. The adapter verifies existing local files, writes only paths
to a protected local owner config
%LOCALAPPDATA%\Fortis\OwnerShell\script-center-launch.json,
prints OWNER_SHELL_PATHS_CONFIGURED=YES and exits WITHOUT starting.
Review the file locally and click Start again to launch the existing
Mirror-owned Start-OwnerPowerShellExecutor.ps1. Ctrl+C stops the session.

The adapter requires a non-admin Windows identity; clean approved
docke-windows-mirror/main pinned to
a5cb3e9f61e68331c11337eb5da4af056d27660c;
approved Git origin; protected key, Bearer, config and receipt DB
ACL; and unoccupied port 10443. It binds ONLY 127.0.0.1:10443,
not an external network interface. It does no Git update, Docker
change, Gateway release, service installation or firewall change.

A new empty receipt DB breaks durable UUID replay and UNCERTAIN
protection: never substitute one. This service grants authenticated
Owner Portal commands arbitrary Windows user-level execution and
is NOT a sandbox. Previous owner TLS material, identity, receipt DB
and Portal session/Origin/CSRF checks require separate owner review.
Windows PowerShell 5.1 live acceptance is not covered by source tests.
