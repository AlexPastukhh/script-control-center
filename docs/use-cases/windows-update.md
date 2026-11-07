# Paired Windows update

## UC-SCC-002

**Когда:** требуется обновить Windows исходники `tunnel` и `script-control-center`
для Repo1/Repo2 либо проверить их согласованность.

**Цель:** обновить только approved clean Windows main clones в правильном порядке.

**Как:** нормативный [paired update owner](../GIT-WINDOWS-SOURCE-OF-TRUTH.md)
и [Tunnel worker chain](https://github.com/AlexPastukhh/tunnel/blob/main/runtime/windows-repo/README.md).
Различать обычный Docker/GitHub цикл UC-CONT-002/003 и специальный guarded
paired update. **Оба канонических Docker и Windows `main` должны оставаться
чистыми**: все edits/commit только в task worktrees; исторические грязные
состояния сначала сохранять в проверенный recovery archive, не вызывать
`reset/stash/clean` автоматически и не публиковать снимки установленного
Windows runtime/секреты. Политика worktrees: [UC-CONT-019](https://github.com/AlexPastukhh/fortis-container/blob/main/docs/use-cases/worktrees.md#uc-cont-019).
Preview перед Apply, проверка exact manifest/clone/ACL/Guard,
не восстанавливать Git PAT helper и не обходить Gateway в Docker. Git source
update не доказывает Windows runtime/reconnect.

**Готово:** owner-verified source hashes, чистые правильные Windows main clones
и нужные Guard Preview; без автоматического запуска/рестарта worker.
