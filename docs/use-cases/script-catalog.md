# Script Catalog

## UC-SCC-001

**Когда:** добавить/обновить/проверить сценарий Script Catalog.

**Цель:** запускать зарегистрированные действия через текущие source/permissions,
не создавать вторую реализацию native Tunnel worker.

**Как:** проверить [README](../../README.md), [Script Catalog source](../../ScriptCatalog.cs)
и [script runner](../../Script-Runner.ps1). Различать регистрацию, Preview и
реальный запуск. Запуск Windows/Tunnel требует отдельной owner авторизации.
Worker chain/Guards являются исходниками `tunnel`, не SCC. Не переносить
в Use Case все каталожные поля или идентификаторы зарегистрированных скриптов.

**Browser Mirror:** новый `Fortis-Mirror-Browser.ps1` — тонкий адаптер
к Windows-клону `docke-windows-mirror/main`; точная одна строка импорта —
`TEXT-IMPORT-MIRROR-BROWSER.txt`, режим `window`, чтобы видеть URL с
одноразовой сессией и ошибки запуска. Старый зарегистрированный путь
Fortis-Windows-Mirror-Status.ps1 теперь по умолчанию открывает тот же Browser.
Явные -Cli/-Notify сохраняют legacy отчёт. Ранее записанную регистрацию
владелец вручную переводит через «Изменить» в «Своё окно»; каталог сам
не мигрирует, новая регистрация не обязательна, службы не перезапускаются.
Импорт в локальный `%LOCALAPPDATA%` выполняет владелец через интерфейс, он
не происходит из Git и не запускает скрипт. `-OpenBrowser` открывает локальный
read-only UI, без fetch/pull/сервисов/активации Gateway.

**Gitleaks:** отдельная ручная запись `window` из
`TEXT-IMPORT-GITLEAKS.txt` → `Start-GitleaksScanner.cmd`, не в preset.
`cmd` не ослабляет PowerShell Execution Policy. Подробности офлайн-сканирования,
приватных JSON и Windows-проверки: [Gitleaks](../GITLEAKS-SCANNER.md).

**Готово:** exact registered item и допустимый runtime/Preview подтверждены,
отсутствие выполнения не подменено успешной регистрацией.

**Qwen MCP:** отдельный запускатель [`Start-Qwen-Fortis-MCP.ps1`](../../Start-Qwen-Fortis-MCP.ps1)
через Windows `py -m uv tool run mcp-proxy`, без Docker/Repo1/Repo2 lifecycle
изменений. Опциональная регистрация через [`TEXT-IMPORT-QWEN-MCP.txt`](../../TEXT-IMPORT-QWEN-MCP.txt),
режим `window`; [порядок и границы](../QWEN-MCP-LAUNCHER.md).
Существующий прокси на 18765 не запускать повторно.
