# Script Center — Windows PowerShell Executor

Сценарии управления **единственным** процессом Windows Executor через существующую регистрацию Script Catalog. Исполнение Machine ASK принадлежит Gateway + Windows Executor, а не Script Center UI. См. [owner launcher](../OWNER-POWERSHELL-EXECUTOR-LAUNCH.md), [opt-in contract](../MACHINE-POWERSHELL-ASK-OPTIN-V1.md) и [Executor use cases](https://github.com/AlexPastukhh/fortis-powershell-executor/blob/main/docs/use-cases/README.md).

<a id="uc-scc-003"></a>

## UC-SCC-003 — Запустить и проверить один Windows Executor

**Когда:** в Script Catalog присутствует «Fortis — PowerShell Executor», требуется проверить Owner-only или Machine ASK.

**Как:**

1. Открыть обычный non-admin Windows Script Center и убедиться, что регистрация ссылается на проверенный Fortis-Owner-PowerShell-Executor.ps1 в clean/synchronized script-control-center/main; source самого Executor — clean/synchronized fortis-powershell-executor/main. Не импортировать второй экземпляр ради диагностики.
2. Первый setup принимает только **пути** пяти ранее установленных файлов: server cert/key, client CA, owner Bearer и **прежней текущей** SQLite. Сохранение в защищённый script-center-launch.json не стартует процесс. Пустую SQLite или изменённые секреты не подменять.
3. Перед запуском независимо убедиться, что прежний Executor действительно остановлен, 10443 закрыт с Windows, нет активных команд/дочерних процессов; недостоверный netstat/отсутствующий PID сам по себе недостаточен. В Script Center нажать **«Запустить» один раз**.
4. Проверить preflight и баннер: Owner-only — **OWNER_SHELL_MTLS_READY (no browser or machine API; do not run elevated)**; Machine ASK — **OWNER_SHELL_MTLS_READY (machine ASK proof verifier enabled; non-admin)**. Баннер Machine ASK дополнить проверкой единственного процесса, прежней SQLite и mTLS из Gateway.
5. Для остановки в отдельном окне исполнителя использовать Ctrl+C **только после owner quiescence**. Не завершать Mirror READ/Browser, Gateway или Script Center целиком; не taskkill/Stop-Process и не автоматический restart.

**Готово:** один проверенный non-admin Executor; прежние TLS/SQLite сохранены; Gateway mTLS verified; статус каталога не подменяет OS/SQLite evidence.

<a id="uc-scc-004"></a>

## UC-SCC-004 — Подключить или отключить отдельный ASK_ONLY opt-in

**Когда:** owner намерен переключить существующий Owner-only Executor в Machine ASK V1 либо вернуться к прежнему режиму.

**Как:**

1. **Отдельный owner GO** после проверки нового Gateway profile и actor/target, защищённого Gateway mount, Windows source pin, mTLS, текущей receipt DB и отсутствия unresolved effects. Не считать source merge/файл конфигурации активацией.
2. В защищённом каталоге OwnerShell должны быть два разных Machine Bearer / Proof Key, отличных от owner Bearer и криптографически согласованных с Gateway. Никогда не печатать содержимое или raw hashes.
3. Проверенный owner-controlled файл machine-script-center.json содержит ровно шесть полей: schemaVersion=1, enabled=true, mode=ASK_ONLY, executorHeadSha=approved main SHA и **пути**, не содержимое, machineBearerFile/machineProofFile. Штатные NTFS ACL, owner, no-reparse и private-path guards обязательны; не использовать ALLOW.
4. Script Center добавляет ровно четыре machine CLI-аргумента только при успешном opt-in. Actor/target зафиксированы в исходниках, а не задаются AI. Сначала мягко остановить старый singleton и подтвердить закрытие 10443; затем одним кликом запустить новый и проверить **OWNER_SHELL_MACHINE_ASK_OPTIN_PREFLIGHT_PASS**, **OWNER_SHELL_SCRIPT_CENTER_PREFLIGHT_PASS** и machine ASK ready banner.
5. Открыть типизированный Gateway capabilities READ: configured=true, policy=ASK, machineExecutionAutoApprove=false, version=1. Затем отдельный [Gateway ASK/Reject acceptance](https://github.com/AlexPastukhh/fortis-approvals/blob/main/docs/POWERSHELL-MACHINE-ASK-CONTRACT-V1.md). Нет прямого owner-only proxy.
6. Для rollback сохранить **ту же текущую** receipt DB, TLS и историю; согласованно остановить singleton, owner-reviewed disable opt-in, запустить Owner-only и проверить его прежний API. Не восстанавливать старую DB копию и не повторять UNCERTAIN.

**Готово:** соответствующий выбранному owner-controlled режиму singleton с проверенным banner, source, ACL, сохранённой SQLite и Gateway TLS. Перезапуск/изменение opt-in никогда не выполняются автоматически.

**Evidence 2026-11-18:** owner подтвердил успешное переключение Machine ASK; Gateway и Windows SQLite подтвердили один новый Get-Date EXECUTED с сохранением прежней квитанции. Полная матрица live fault-injection пока не пройдена.
