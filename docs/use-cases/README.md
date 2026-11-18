# Script Control Center — локальные Use Cases

Общие Fortis правила: [главный реестр](https://github.com/AlexPastukhh/fortis-container/blob/main/docs/use-cases/README.md),
полное обязательное чтение UC-CONT-002/003. Этот реестр определяет только
повторяемые сценарии Script Catalog, paired Windows updater и управления Executor.

| ID | Когда | Результат | Владелец |
| --- | --- | --- | --- |
| UC-SCC-001 | Управлять Script Catalog | Принятая регистрация/запуск скрипта без смены worker source | [Catalog](script-catalog.md#uc-scc-001) |
| UC-SCC-002 | Обновить Windows Tunnel + SCC clones | Manifest-проверенный guarded update | [Windows update](windows-update.md#uc-scc-002) |
| UC-SCC-003 | Запустить/остановить один Windows Executor | Non-admin singleton, текущая SQLite и Gateway mTLS | [Executor](executor.md#uc-scc-003) |
| UC-SCC-004 | Включить или отключить Machine ASK V1 | Protected ASK_ONLY opt-in, owner-controlled switch и проверка rollback | [Executor](executor.md#uc-scc-004) |
