# Qwen MCP launcher for Script Catalog

`Start-Qwen-Fortis-MCP.ps1` runs the **owner-tested** Windows invocation
`py -m uv tool run mcp-proxy` with `docker exec` into the **existing**
`fortis-repo-test` container, launching the original installed Desktop Commander
0.2.52 process as container UID/GID 1000. SSE binds to loopback only:
`http://127.0.0.1:18765/sse` (26 available MCP tools).

## Register manually after approved GitHub merge and Windows main update

Open Script Catalog -> **«Импорт из текста»**, copy the non-comment line of
`TEXT-IMPORT-QWEN-MCP.txt`, then **«Проверить» -> «Импортировать»**.
The entry **«Qwen — Fortis MCP»** uses `window` / **«Своё окно»** so that
proxy stdout/stderr remain visible. It is **not** part of the five default
registrations. Import merely registers; click **«▶ Запустить»** to start it.
The Catalog does not capture logs for `window` mode.

## Prerequisites and lifecycle

Windows PowerShell 5.1; `py.exe`, Python-installed `uv` (`py -m uv --version`),
`docker.exe` on PATH, the already running `fortis-repo-test` container, and
TCP port 18765 not already listening. The script fails closed if any check
fails. `uv` may download the proxy package on the first run, but the adapter
**never itself installs `uv`**, modifies the Windows PATH or fetches Git.

If a manually started Qwen proxy already listens on 18765, **do not start a
second copy**. Stop only that proxy with Ctrl+C in its own window before
launching from the catalog; do not stop/restart the shared Docker container.
Keep the new window open while Qwen needs the MCP. Ctrl+C stops only this
proxy. The script never changes Qwen Studio's saved SSE config, Repo1/Repo2
workers, Docker container lifecycle or Qwen's legacy Workfiles artifacts.
A TCP listener alone does not prove the port belongs to this MCP; check it
before stopping any process.

**Permissions:** this is the **full Desktop Commander** route, not the
historical six-tool read-only `gateway-stdio.mjs`. It can read/write shared
workspace files and execute processes as UID 1000. Keep first smoke tests
read-only. Windows launching and live Qwen behavior need owner acceptance;
Linux static tests cannot prove them.
