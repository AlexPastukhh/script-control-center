# Backlog improvements — Script Control Center

**Единый файл будущих улучшений этого репозитория.** Здесь только Script Catalog/UI, его Windows updater и тонкие adapters. Shared Repo1/Repo2 Guards, worker policy и runtime находятся в `tunnel`; общее Docker/host-only — в `fortis-container`. Выполненная работа фиксируется отдельными evidence. Backlog не разрешает Windows/Production mutation.

## Приоритеты

- Упростить одну детерминированную owner-run цепочку `tools/Update-Windows-ControlCenter.ps1`: read-only Preview → отдельный `-Apply` с проверкой точных SHAs **обоих** Windows clones, девятифайлового manifest, PowerShell parser/WinForms compile и read-only Repo1+Repo2 Guard Previews. Не добавлять второй Windows updater или вторые копии policy.
- Перед дальнейшим reimport/reconnect или исправлением ярлыка сверить live owner-clone, состояние обеих workers, активные профили, разрешённые сценарии и сохранность другого работающего подключения; обновление Git source **не означает** перезапуск и не должно его автоматически выполнять.
- Улучшить диагностику Script Catalog: ясно отделять `Не проверен`, `Работает (внешний)`, multiple external processes и реально остановленные процессы. Никогда не завершать PID без повторной точной проверки owner/session/script path.
- Добавить систематические негативные/позитивные тесты для resolver, manifests и запрета fallback на старые `launchers/`; source-only тесты не выдавать за Windows live acceptance.
- Для действий, недоступных Docker, дать одну короткую owner-команду по `UC-CONT-025`, а передачу пакета — по `UC-CONT-015`, без произвольного Shell/Docker доступа AI.

**Текущие технические владельцы:** [`docs/GIT-WINDOWS-SOURCE-OF-TRUTH.md`](GIT-WINDOWS-SOURCE-OF-TRUTH.md), [`README.md`](../README.md), `tunnel/runtime/windows-repo/README.md`. Исторические implementation plans в `docs/history/` не являются вторым living backlog.

## Historical GO3 helpers

- Before reusing `_go3-revision06/07` for a new runtime, review their pinned
  source/container assumptions and decide whether a maintained helper is needed.
  Routine paired updates already have their own Preview/Apply controller; do not
  create a second updater or auto-rewrite historical pins.
