# CHANGELOG - AAEmu 5.0.7.0 Private Server

## [5.0.7.0 private] - 15.07.2026

### Fixes / Исправления

**fix(patches): внедрить набор патчей AAFree 5.0 от devincean-gif**

На русском:
- Внедрены серверные патчи из проекта `devincean-gif/archeage-v5.0-patches`
- Автор патчей: `devincean-gif`
- Исправлены сетевые и runtime-проблемы: защита `PacketStream.Roundup`, crash-guard для неверного packet level, снижение scanner/unknown-packet log flood
- Добавлены и адаптированы патчи боевой системы: continuous auto-attack, hit FX для auto-attack, корректная обработка `SCSkillFiredPacket`
- Добавлены исправления питомцев и смерти: mate death/revive cooldown, revive через HealPet, level-scaled death penalty и recoverable exp
- Добавлены quest/cinema исправления: directing-mode cinema, vehicle dismount на cinematic, report NPC group, item group gather/use acts, rejected quest-offer freeze guard, sphere component matching
- Добавлены world/vehicle/NPC исправления: cave floor clamp, recorded nav floor для движения и спавна, terrain preloader, stuck rider/player recovery, vehicle OOB guard, vehicle world streaming
- Добавлены gathering и well/self-cast use исправления
- Обновлены `NLog.config`, `World.json`, `CharacterSettings.json`, `ExampleConfig.json`
- Добавлены SQL-патчи и canonical dump updates для `aaemu_game_5070`: `mates.revive_time`, Loyalty marketplace catalog, compact SQLite `npc_mount_skills` dedup
- Добавлен client-pak патч `Docs/client-patches/vehicle-summon-reading-roll.patch`

English:
- Applied server patch set from `devincean-gif/archeage-v5.0-patches`
- Patch author: `devincean-gif`
- Fixed networking/runtime issues: `PacketStream.Roundup` overflow guard, invalid packet-level crash guard, scanner/unknown-packet log flood reduction
- Added combat fixes: continuous auto-attack, auto-attack hit FX, `SCSkillFiredPacket` handling
- Added mate/death fixes: mate death/revive cooldown, HealPet revive, level-scaled death penalty and recoverable exp
- Added quest/cinema fixes: directing-mode cinema, cinematic vehicle dismount, report NPC group, item group gather/use acts, rejected quest-offer freeze guard, sphere component matching
- Added world/vehicle/NPC fixes: cave floor clamp, recorded nav floor for movement/spawn, terrain preloader, stuck rider/player recovery, vehicle OOB guard, vehicle world streaming
- Added gathering and well/self-cast use fixes
- Updated `NLog.config`, `World.json`, `CharacterSettings.json`, `ExampleConfig.json`
- Added SQL patches and canonical dump updates for `aaemu_game_5070`: `mates.revive_time`, Loyalty marketplace catalog, compact SQLite `npc_mount_skills` dedup
- Added client-pak patch `Docs/client-patches/vehicle-summon-reading-roll.patch`

Source: `https://github.com/devincean-gif/archeage-v5.0-patches`

---

## [Unreleased]

## [5.0.7.0 private] - 14.06.2026

### Bug Fixes / Исправления

**fix(appellation): исправить применение баффа при смене звания — сделать buff_id серверно-авторитетным**

- `addBuff` получал ID титула, но вызывался с сырым ID баффа из клиентского пакета — `GetAppellationsTemplate(buffId)` всегда возвращал `null`, бафф никогда не применялся
- `Change()` и `Load()` теперь определяют `BuffId` из серверного шаблона `AppellationTemplate.BuffId`, а не из клиентских данных
- Добавлено предупреждение при несовпадении клиентского `buff_id` с серверным значением для защиты от подмены
- `addBuff` рефакторен: теперь принимает сырой ID баффа напрямую, без поиска по шаблону
- Добавлена SQL-миграция: колонка `buff_id` в таблице `appellations`
- Обновлены юнит-тесты: используется Moq для проверки вызова `AddBuff` с корректным ID

Files changed: `AAEmu.Game/Models/Game/Char/CharacterAppellations.cs`, `AAEmu.Game/SQL/updates/2026-06-14_aaemu_game_appellations_add_buff_id.sql`, `AAEmu.UnitTests/Game/Models/Game/Char/CharacterAppellationsTests.cs`

Docs added: `Docs/appellation-buff-id-server-authoritative-2026-06-14.md`

---

**fix(ai-behavior): добавить проверку CC-эффектов в PickSkillAndUseIt, убрать dead code**

На русском:
- Добавлена проверка блокировки каста навыков при эффектах контроля толпы (Knockdown/Sleep/Stun) в методе `PickSkillAndUseIt` — ранее NPC могли использовать навыки будучи оглушёнными, спящими или сбитыми с ног
- Проверка выполняется инлайн через `HasEffectsMatchingCondition`, что соответствует существующему паттерну в `BaseCombatBehavior.CanUseSkill` и `Npc.MoveTowards`
- Удалён неиспользуемый кэширующий флаг `_isCrowdControlBlocked` и метод `UpdateCrowdControlState()`, который нигде не вызывался (dead code)
- Добавлена null-safe проверка `IsDead` (`Ai.Owner?.IsDead ?? true`) для предотвращения `NullReferenceException` при отсутствии владельца AI

English:
- Added crowd control check (Knockdown/Sleep/Stun) in `PickSkillAndUseIt` — previously NPCs could use skills while stunned, sleeping, or knocked down
- Check is performed inline via `HasEffectsMatchingCondition`, matching the existing pattern in `BaseCombatBehavior.CanUseSkill` and `Npc.MoveTowards`
- Removed unused `_isCrowdControlBlocked` cache field and `UpdateCrowdControlState()` method that was never called (dead code)
- Added null-safe `IsDead` check (`Ai.Owner?.IsDead ?? true`) to prevent `NullReferenceException` when AI owner is missing

Files changed: `AAEmu.Game/Models/Game/AI/v2/Framework/Behavior.cs`

---

## [5.0.7.0 private] - 13.06.2026

### Features / Новые функции

**add(geo): Применить патч 6_AAEmu_pathfinding_V6_1_FULL by Guyldorum**

На русском:
- Применён патч 6_AAEmu_pathfinding_V6_1_FULL от Guyldorum
- Значительное расширение геодаты и pathfinding: `AiGeodataManager`, `PathNode`, `BaseCombatBehavior`
- Новые команды: `/heightmob`, расширения `/pathmob` и `/astar`
- Новые конфигурации: `Configurations.cs`, `World.json`
- Добавлена диагностика спавна: `SpawnHeightDiagnostics.cs`, `TrackAndStoreCoordinates.cs`
- Добавлен анализ архитектуры: `Docs/analysis/00-architecture-review.md`

English:
- Applied patch 6_AAEmu_pathfinding_V6_1_FULL by Guyldorum
- Significant geodata and pathfinding expansion: `AiGeodataManager`, `PathNode`, `BaseCombatBehavior`
- New commands: `/heightmob`, extended `/pathmob` and `/astar`
- New configurations: `Configurations.cs`, `World.json`
- Added spawn diagnostics: `SpawnHeightDiagnostics.cs`, `TrackAndStoreCoordinates.cs`
- Added architecture review: `Docs/analysis/00-architecture-review.md`

Files changed: `AAEmu.Game/Configurations/World.json`, `AAEmu.Game/Core/Managers/AiGeodataManager.cs`, `AAEmu.Game/Core/Managers/SpawnHeightDiagnostics.cs`, `AAEmu.Game/Models/Game/AI/AStar/PathNode.cs`, `AAEmu.Game/Models/Game/AI/v2/Behaviors/BaseCombatBehavior.cs`, `AAEmu.Game/Models/Game/AI/v2/Behaviors/Flytrap/FlytrapAttackBehavior.cs`, `AAEmu.Game/Models/Game/Configurations.cs`, `AAEmu.Game/Models/Game/NPChar/Npc.cs`, `AAEmu.Game/Models/Game/NPChar/NpcSpawnerNpc.cs`, `AAEmu.Game/Models/Game/NPChar/TrackAndStoreCoordinates.cs`, `AAEmu.Game/Scripts/Commands/Height.cs`, `AAEmu.Game/Scripts/Commands/HeightMob.cs`, `AAEmu.Game/Scripts/Commands/PathMob.cs`, `AAEmu.Game/Scripts/Commands/AStar/AStarEndPositionSubCommand.cs`, `AAEmu.Game/Scripts/Commands/AStar/AStarPathFindingSubCommand.cs`, `AAEmu.Game/Scripts/Commands/AStar/AStarStartPositionSubCommand.cs`, `AAEmu.Game/Scripts/Commands/AStar/AStarViewSubCommand.cs`, `Docs/analysis/00-architecture-review.md`

Tests added:
- `AAEmu.UnitTests/Game/Core/Managers/SpawnHeightDiagnosticsTests.cs` — 17 tests covering Record/TryGet/Remove/CleanupOldEntries/Clear, Entry properties, EntryTtl
- `AAEmu.UnitTests/Game/Models/Game/NPChar/BuildingTests.cs` — 14 tests covering GetFloorHeight by index and by reference Z, edge cases
- `AAEmu.UnitTests/Game/Models/Game/SpawnHeightConfigTests.cs` — 15 tests covering all default values and setters
- `AAEmu.UnitTests/Game/Models/Game/PathingConfigTests.cs` — 20 tests covering all default values and setters
- `AAEmu.UnitTests/Game/Models/Game/Ai/AStar/AStarHeapTests.cs` — 16 tests covering heap operations, ordering (F→H→Seq), dynamic resizing, AStarEntry struct
- `AAEmu.UnitTests/Game/Models/Game/Ai/AStar/PathNodeDiagnosticsTests.cs` — 12 tests covering all diagnostic fields, PathStatus enum, EstimateFullPathLength
- `AAEmu.UnitTests/Game/Models/Game/WorldConfigPathingTests.cs` — 6 tests covering WorldConfig.SpawnHeight and WorldConfig.Pathing defaults and assignment

### Refactoring / Рефакторинг

**refactor(ai-behavior): заменить хардкод константы MeleeAttackRange на настраиваемый параметр**

На русском:
- Убрана захардкоженная константа `MeleeAttackRange = 4f` в `Behavior.cs` — теперь используется `Ai.Param.MeleeAttackRange` (по умолчанию 4f, настраивается через `data.meleeAttackRange` в JSON NPC-параметров)
- Исправлен баг в `SetWeaponRange`: метод использовал экипировку **цели** вместо **атакующего** — теперь корректно берётся оружие `Ai.Owner`
- Добавлена константа `FlyingHeightGapMultiplier = 3.5f` для вертикального обнаружения летающих NPC — семантически отделена от ближнего боя
- В `Skill.cs CalculateRange` добавлена safety-проверка для базовых навыков ближнего боя (ID 2/3/4) без настроенного слота оружия — если `Template.MaxRange > 5.0`, ограничивается до 4f
- Упрощена логика fallback в `CalculateRange` — убран избыточный ветвящийся код для melee/non-melee
- Удалены неиспользуемые импорты (`System`, `System.Linq`, `System.Collections.Generic`, `System.Threading.Tasks`) из `Behavior.cs` и `Skill.cs`
- Исправлена отступка в блоках `else` в `Skill.cs` (`ApplyEffects`, проверка реагентов)

English:
- Removed hardcoded `MeleeAttackRange = 4f` constant in `Behavior.cs` — now uses `Ai.Param.MeleeAttackRange` (default 4f, configurable via `data.meleeAttackRange` in NPC AI params JSON)
- Fixed bug in `SetWeaponRange`: method was using the **target's** equipment instead of the **attacker's** — now correctly reads `Ai.Owner` equipment
- Added `FlyingHeightGapMultiplier = 3.5f` constant for flying NPC vertical detection — semantically separated from melee range
- Added safety clamp in `Skill.cs CalculateRange` for melee base skills (ID 2/3/4) without a weapon slot configured — if `Template.MaxRange > 5.0`, clamped to 4f
- Simplified fallback logic in `CalculateRange` — removed redundant branching for melee/non-melee
- Removed unused imports (`System`, `System.Linq`, `System.Collections.Generic`, `System.Threading.Tasks`) from `Behavior.cs` and `Skill.cs`
- Fixed indentation in `else` blocks in `Skill.cs` (`ApplyEffects`, reagent checking)

Files changed: `AAEmu.Game/Models/Game/AI/v2/Framework/Behavior.cs`, `AAEmu.Game/Models/Game/AI/v2/Behaviors/WildBoar/WildBoarAttackBehavior.cs`, `AAEmu.Game/Models/Game/Skills/Skills.cs`

Docs added: `Docs/analysis/05-ai-behavior-refactor-review.md`

---

## [5.0.7.0 private] - 03.06.2026 (6)

### Bug Fixes / Исправления

**fix(npc-movement): защитить Z-координату НПС при движении на поднятых площадках**

На русском:
- `Npc.StopMovement()`, `Npc.LookTowards()` и `Simulation.cs` безусловно вызывали `GetReferenceHeight`, который возвращает высоту рельефа — НПС на поднятых помостах «проваливался» на уровень земли при остановке или смене направления
- Теперь вызов `GetReferenceHeight` защищён проверкой якоря: если `HeightState.AnchorKind != Terrain` и якорь инициализирован, Z не перезаписывается
- Дополнительно `CalcNextRoamingPosition` (AIUtils.cs) сохраняет `IdlePosition.Z` для НПС с якорем не Terrain, чтобы цель блуждания была на правильной высоте
- `RoamingBehavior` использует 3D-расстояние (`includeZAxis=true`) для проверки достижения цели — исключает ложное срабатывание «дошёл до цели», когда НПС находится над/под точкой назначения

English:
- `Npc.StopMovement()`, `Npc.LookTowards()` and `Simulation.cs` unconditionally called `GetReferenceHeight`, which returns terrain height — NPCs on raised platforms/floors were snapped to terrain Z on every stop or turn
- The `GetReferenceHeight` call is now guarded: if `HeightState.AnchorKind != Terrain` and the anchor is initialized, Z is preserved as-is
- `CalcNextRoamingPosition` (AIUtils.cs) keeps `IdlePosition.Z` for non-Terrain-anchored NPCs so roaming targets are placed at the correct elevated height
- `RoamingBehavior` uses 3D distance (`includeZAxis=true`) for arrival detection — prevents false "reached destination" when the NPC is above/below the target

Files changed: `AAEmu.Game/Models/Game/NPChar/Npc.cs`, `AAEmu.Game/Models/Game/Units/Route/Simulation.cs`, `AAEmu.Game/Models/Game/AI/Utils/AIUtils.cs`, `AAEmu.Game/Models/Game/AI/v2/Behaviors/Common/RoamingBehavior.cs`

Tests added: `AAEmu.UnitTests/Game/Models/Game/NPChar/NpcHeightAnchorTests.cs`

Docs added: `Docs/npc-height-anchor-and-worldtemplate-initialization-2026-06-03.md`

---

## [5.0.7.0 private] - 03.06.2026 (5)

### Bug Fixes / Исправления

**fix(geodata): исправить вычисление высоты рельефа при спавне НПС и в геоданных**

На русском:
- `NpcSpawnHeightOverrides.GetSpawnHeight` использовал `world.Template?.GetHeight()` — этот метод использует `WorldCell.HeightMap`, загружаемый из per-cell `heightmap.dat`. Если эти файлы недоступны в клиентских данных, возвращается 0, что нарушало логику определения якоря: все НПС воспринимались как «подвешенные высоко над рельефом» и получали якорь `Spawner` вместо `Terrain`
- Заменено на `world.GetHeight(x, y)` — использует `World.HeightMaps`, загружаемый глобально при старте сервера, гарантированно содержит данные
- То же исправление в `WorldStaticGeometryIndex.TryCollectLayerHeights` и `TryGetClosestDistinctLayerPoint`
- Убраны избыточные проверки `world?.Template == null` в этих методах — они работают без WorldTemplate
- Также исправлен `IndexOutOfRangeException` в `WorldTemplate.GetBaiByPos` и `GetRawHeightMapHeight`: граничные проверки `> CellX/CellY` заменены на `>= CellX/CellY`, добавлена проверка bounds в `GetBaiByPos`. Краш происходил в каждом тике `MoveTowards`, прерывая движение всех НПС

English:
- `NpcSpawnHeightOverrides.GetSpawnHeight` used `world.Template?.GetHeight()` — backed by `WorldCell.HeightMap` from per-cell `heightmap.dat`. When those files are absent from the client bundle, the method returns 0, breaking anchor logic: all NPCs appeared "suspended high above terrain" and got `Spawner` instead of `Terrain` anchor
- Replaced with `world.GetHeight(x, y)` — backed by `World.HeightMaps`, the global heightmap loaded at startup, always populated
- Same fix in `WorldStaticGeometryIndex.TryCollectLayerHeights` and `TryGetClosestDistinctLayerPoint`
- Removed redundant `world?.Template == null` guards in these methods — they work without WorldTemplate
- Also fixed `IndexOutOfRangeException` in `WorldTemplate.GetBaiByPos` and `GetRawHeightMapHeight`: boundary checks `> CellX/CellY` changed to `>= CellX/CellY`, bounds guard added in `GetBaiByPos`. The crash was firing every `MoveTowards` tick, aborting movement for all NPCs

Files changed: `AAEmu.Game/Models/Game/NPChar/NpcSpawnHeightOverrides.cs`, `AAEmu.Game/Physics/WorldStaticGeometryIndex.cs`, `AAEmu.Game/Models/Game/World/WorldTemplate.cs`

---

## [5.0.7.0 private] - 03.06.2026 (4)

### Bug Fixes / Исправления

**fix(geodata): создавать WorldTemplate и WorldCell при загрузке мира**

На русском:
- `World.Template` никогда не присваивался → всегда `null`
- Следствие: `IngestObjectDatIntoStaticGeometryIndex` скипал все миры (проверка `world.Template?.Id != Template.Id` → всегда `true`) → `StaticGeometryIndex` всегда пустой → `TryRaycastDown` ничего не находит → НПС не получают якорь `InteriorFloor`
- Следствие: `world.Template?.GetHeight(...)` возвращал `null` → `VerifyCellLoaded()` никогда не вызывался → `object.dat`/`statobjs.dat` никогда не загружались → это объясняет почему сервер загружается очень быстро по сравнению с 3.5
- Следствие: `PhysicsManager.InitializeTerrain()` пропускался (`SimulationWorld?.Template == null`)
- Исправление: в `WorldManager.Load()`, после создания `World`, создаётся `WorldTemplate` с нужными полями, массив `WorldCell[CellX, CellY]` заполняется экземплярами (незагруженными — данные загружаются лениво при первом обращении), и присваивается `world.Template`

English:
- `World.Template` was never assigned → always `null`
- Consequence: `IngestObjectDatIntoStaticGeometryIndex` skipped all worlds (`world.Template?.Id != Template.Id` always `true`) → `StaticGeometryIndex` always empty → `TryRaycastDown` finds nothing → NPCs never get `InteriorFloor` anchor
- Consequence: `world.Template?.GetHeight(...)` returned null → `VerifyCellLoaded()` never called → `object.dat`/`statobjs.dat` never loaded → explains why server starts much faster than 3.5
- Consequence: `PhysicsManager.InitializeTerrain()` was skipped (`SimulationWorld?.Template == null`)
- Fix: in `WorldManager.Load()`, after creating the `World`, create a `WorldTemplate` with matching properties, populate `WorldCell[CellX, CellY]` with unloaded cell instances (data loads lazily on first access), and assign `world.Template`

Files changed: `AAEmu.Game/Core/Managers/World/WorldManager.cs`

---

## [5.0.7.0 private] - 03.06.2026 (3)

### Bug Fixes / Исправления

**fix(npc-movement): не сбрасывать Z НПС на высоту рельефа при движении/остановке на поднятых полах**

На русском:
- `Npc.StopMovement()`, `Npc.LookTowards()` и `Simulation.cs` безусловно вызывали `GetReferenceHeight`, который возвращает высоту рельефа — НПС на поднятых помостах «проваливался» на уровень земли при остановке или смене направления
- Теперь вызов `GetReferenceHeight` защищён проверкой якоря: если `HeightState.AnchorKind != Terrain` и якорь инициализирован, Z не перезаписывается
- Дополнительно `CalcNextRoamingPosition` (AIUtils.cs) сохраняет `IdlePosition.Z` для НПС с якорем не Terrain, чтобы цель блуждания была на правильной высоте
- `RoamingBehavior` использует 3D-расстояние (`includeZAxis=true`) для проверки достижения цели — исключает ложное срабатывание «дошёл до цели», когда НПС находится над/под точкой назначения

English:
- `Npc.StopMovement()`, `Npc.LookTowards()` and `Simulation.cs` unconditionally called `GetReferenceHeight`, which returns terrain height — NPCs on raised platforms/floors were snapped to terrain Z on every stop or turn
- The `GetReferenceHeight` call is now guarded: if `HeightState.AnchorKind != Terrain` and the anchor is initialized, Z is preserved as-is
- `CalcNextRoamingPosition` (AIUtils.cs) keeps `IdlePosition.Z` for non-Terrain-anchored NPCs so roaming targets are placed at the correct elevated height
- `RoamingBehavior` uses 3D distance (`includeZAxis=true`) for arrival detection — prevents false "reached destination" when the NPC is above/below the target

Files changed: `AAEmu.Game/Models/Game/NPChar/Npc.cs`, `AAEmu.Game/Models/Game/Units/Route/Simulation.cs`, `AAEmu.Game/Models/Game/AI/Utils/AIUtils.cs`, `AAEmu.Game/Models/Game/AI/v2/Behaviors/Common/RoamingBehavior.cs`

---

## [5.0.7.0 private] - 03.06.2026 (2)

### Bug Fixes / Исправления

**fix(geodata): исправить путь к CGF-моделям brush-объектов через statobjs.dat**

На русском:
- `WorldCell.IngestObjectDatIntoStaticGeometryIndex` использовал `objectsFile.AssetPathsList[brush.PathId]` (список материалов/текстур) вместо CGF-путей — в результате `CryEngineModelHelper.MakeModel` получал неверный путь и возвращал пустую геометрию
- Добавлена загрузка `statobjs.dat` рядом с `object.dat`: файл содержит список CGF-путей в том же формате, что `MaterialsFile`, индексированный по `brush.PathId`
- Теперь путь к модели берётся из `statObjsFiles.MaterialList[brush.PathId]` — реальная геометрия brush-объектов загружается корректно, raycast `TryRaycastDown` находит полы зданий/помостов

English:
- `WorldCell.IngestObjectDatIntoStaticGeometryIndex` was indexing into `objectsFile.AssetPathsList` (material/texture paths) with `brush.PathId`, so `CryEngineModelHelper.MakeModel` received the wrong path and returned empty triangles — static floor geometry was missing
- Added loading of `statobjs.dat` (same directory as `object.dat`): this file holds the list of CGF model paths, identical in format to `MaterialsFile`, indexed by `brush.PathId`
- Model path is now taken from `statObjsFiles.MaterialList[brush.PathId]`; real brush geometry loads correctly and `TryRaycastDown` can now hit building floors and raised platforms

Files changed: `AAEmu.Game/Models/Game/World/WorldCell.cs`

---

## [5.0.7.0 private] - 03.06.2026

### Features / Новые функции

**feat(geodata): добавить статическую клиентскую геодату (фазы 1–11)**

#### Что реализовано

**Ядро: `WorldStaticGeometryIndex` + `StaticGeoDataService`** (`AAEmu.Game/Physics/WorldStaticGeometryIndex.cs`)
- Пространственный индекс по сетке ячеек (64×64 у.е.) для brush/voxel объектов клиентского мира
- `TryRaycastDown` / `TryCollectRaycastDownHits` — вертикальный raycast вниз, возвращает все попадания (multi-hit для стопок геометрии)
- `IsSegmentWallBlocked` — проверка горизонтального сегмента на пересечение со статическими стенами
- `StaticGeoDataService.TryResolveDistinctLayerHeight` — определение высоты обособленного слоя (пол помещения, пещера, возвышенность)

**Интеграция в мир** (`World.cs`, `WorldCell.cs`, `PhysicsManager.cs`)
- `World.Physics.StaticGeometryIndex` — индекс регистрируется в `PhysicsManager`, ячейки заполняются при загрузке
- `World.IsStaticCollisionSegmentBlocked(start, end, tolerance)` — проверка движения NPC с кешированием результатов (500 мс TTL)
- `World.GetHeight(x, y, referenceZ)` — перегрузка для высоты, консультирующая статическую геометрию (multi-hit) перед возвратом к heightmap
- `WorldManager.GetHeight(zoneId, x, y, referenceZ)` — передаёт `referenceZ` в новый `World.GetHeight`

**AI: убраны runtime-guards** (`BaseCombatBehavior.cs`, `FlytrapAttackBehavior.cs`)
- Убраны проверки `AppConfiguration.Instance.World.GeoDataMode` из боевого AI (геодата всегда доступна)
- Добавлено статическое определение маршрута: `IsStaticCollisionSegmentBlocked` используется перед Navmesh

**AI: hybrid path / навмеш** (`PathNode.cs`, `AiGeodataManager.cs`)
- `AiGeodataManager.IsSegmentBlocked(start, end)` — проверяет сегмент через все активные миры
- `PathNode`: hybrid runtime validation — фильтрация линков через статическую геометрию

**NPC высота** (`NpcHeightResolver.cs`, `NpcHeightState.cs`, `NpcSpawnHeightOverrides.cs`, `NpcWaterSurfaceOverrides.cs`, `NpcPathfindingImproved.cs`, `NpcDoodadObstacleClassifier.cs`)
- `NpcHeightAnchorKind` — enum типа anchor (Terrain, InteriorFloor, WaterSurface, WaterColumn, Air, Spawner, Unknown)
- `NpcHeightState` — отслеживание текущего anchor'а NPC, LockLayer для пещер/зданий
- `NpcHeightResolver` — определение высоты NPC с учётом слоёв геометрии
- `NpcSpawnHeightOverrides` — разрешение высоты спавна с приоритетом IntegriorFloor
- `NpcPathfindingImproved` — улучшенный pathfinding: terrain wall detours (фаза 5), shared world mesh fallback (фаза 11)

**Конфигурация** (`Configurations.cs`)
- `GeoDataMode = true` по умолчанию; обновлён комментарий: legacy BAI/nav toggle

#### Что не портировалось (отсутствует контекст в цели)
- `AIUtils.TryFindIndoorRoamingPosition` (фаза 10) — метод отсутствует в упрощённой версии цели
- `Npc.FindPath2` / `TryGetFindPath2BlockingInfo` (фазы 6–7) — методы отсутствуют в цели
- `NpcSpawnerNpc` height resolution block (фаза 2) — не реализована в целевом SpawnNpc

Files changed: `WorldStaticGeometryIndex.cs` (new), `World.cs`, `WorldCell.cs`, `PhysicsManager.cs`, `AiGeodataManager.cs`, `BaseCombatBehavior.cs`, `FlytrapAttackBehavior.cs`, `PathNode.cs`, `Configurations.cs`, `WorldManager.cs`, `NpcHeightResolver.cs` (new), `NpcHeightState.cs` (new), `NpcSpawnHeightOverrides.cs` (new), `NpcWaterSurfaceOverrides.cs` (new), `NpcPathfindingImproved.cs` (new), `NpcDoodadObstacleClassifier.cs` (new), `BaseBaiLoader.cs`, `AiShape.cs`, `AppConfiguration.NpcPathfinding.cs` (new), `NpcAi.cs`

Tests added: `WorldInstanceStaticGeometrySegmentTests.cs`, `WorldGetHeightStaticGeoTests.cs`, `StaticGeoDataServiceTests.cs`, `AiGeodataManagerTests.cs`, `PathNodeTests.cs`, `AIUtilsTests.cs`, `BaseCombatBehaviorTests.cs`, `NpcPathfindingImprovedTests.cs`, `WorldTemplateBoundsTests.cs`, `NpcHeightAnchorTests.cs`

---

## [5.0.7.0 private] - 02.06.2026

### Bug Fixes / Исправления

**fix(appellation): передавать buffId звания в пакет SCAppellationChanged** *(by BwonSamdi)*

- `SCAppellationChangedPacket` дополнен полем `buffId` — клиент ожидает его при смене звания
- В `CharacterAppellations.Change()` перед широковещательной рассылкой читается `BuffId` из шаблона активного звания и передаётся в пакет

Files changed: `AAEmu.Game/Core/Packets/G2C/SCAppellationChangedPacket.cs`, `AAEmu.Game/Models/Game/Char/CharacterAppellations.cs`

---

**fix(physics): исправить погружение и выныривание кораблей при призыве**

На русском:
- Исправлена видимая анимация погружения/выныривания корабля сразу после окончания PortalTime
- Причина: **гонка потоков** — `Build()` добавляет тело в Jitter с `AffectedByGravity = true` по умолчанию; между `Build()` и `AddForRectangularParallelepiped()` физический поток успевал сделать тик, применяя гравитацию без буоянси → тело получало ~0.5 м/с вниз
- Так как `SyncTransformWithRigidBody` пропускается во время PortalTime, за 5-10 секунд корабль уходил на ~2.5м вниз; после окончания PortalTime клиент получал реальную позицию → видимый прыжок вниз
- Решение 1: сразу после `Build()` устанавливать `Hull.AffectedByGravity = false` и обнулять скорости
- Решение 2: в `Buoyancy.PreStep` во время PortalTime обнулять скорости (страховка от любых других источников импульсов)
- Дополнительно: `CreateWaterAndLandSurfaceCache()` вызывается в `AddShip()` для корректного `CachedWaterSurface` с первого тика

English:
- Fixed visible submersion/surfacing of ships right after PortalTime ends
- Root cause: **thread race** — `Build()` adds the hull to Jitter with `AffectedByGravity = true`; between `Build()` and `AddForRectangularParallelepiped()` the physics thread could run one step applying gravity with no buoyancy → hull gained ~0.5 m/s downward
- Since `SyncTransformWithRigidBody` is skipped during PortalTime, over 5-10 s the body drifted ~2.5 m down; when PortalTime ended the client received the real (lower) position → visible dive
- Fix 1: set `Hull.AffectedByGravity = false` and zero velocity/angular velocity immediately after `Build()`
- Fix 2: in `Buoyancy.PreStep` during PortalTime, zero velocity each tick (safety net for terrain impulses etc.)
- Also: `CreateWaterAndLandSurfaceCache()` now called in `AddShip()` so `CachedWaterSurface` is valid from tick 1

Files changed: `AAEmu.Game/Core/Managers/World/PhysicsManager.cs`, `AAEmu.Game/Physics/Forces/Buoyancy.cs`

Tests added (`AAEmu.UnitTests/Game/Physics/ShipPhysicsTests.cs`, 5 новых `[Fact]`):
- `BuoyancyPreStep_DuringPortalTime_SetsGravityFalseAndZerosVelocity`
- `BuoyancyPreStep_AfterPortalTime_EnablesGravity`
- `BuoyancyEquilibrium_AtTargetDepth_BuoyancyForceEqualsGravityForce`
- `ShipController_Build_DefaultGravityIsTrue_RequiresExplicitDisableInAddShip`
- `BuoyancyEquilibrium_TargetSpawnZ_IsSlightlyBelowWaterSurface`

Docs added: `Docs/ship-spawn-dive-fix-2026-06-02.md`

---

## [5.0.7.0 private] - 31.05.2026

### Bug Fixes / Исправления

**fix(ship-breathing): не проверять дыхание персонажа на палубе корабля** (b919a2392)

На русском:
- Исправлена проблема, когда персонаж задыхался, находясь на палубе корабля
- Причина: система проверяла глобальную Z координату, но не учитывала, что персонаж на ПАЛУБЕ корабля
- Пример: корабль на высоте 91, палуба на +6 от центра → глобальная Z = 97 < пороговое значение (98) → ошибочное дыхание
- Решение: если персонаж на кораблле (ParentObj is Slave), проверяем дыхание только если Local.Z < -5 (внутри корабля)
- На палубе или выше (Local.Z >= -5) персонаж не может быть под водой

English:
- Fixed character drowning while standing on ship deck
- Root cause: breathing check compared global Z coordinate without considering character was ON deck
- Example: ship at height 91, deck at +6 from center → global Z = 97 < threshold (98) → false drowning
- Solution: if character on ship (ParentObj is Slave), only check breathing if Local.Z < -5 (inside ship)
- On deck or above (Local.Z >= -5) character cannot be underwater

Files changed: `AAEmu.Game/Models/Game/Units/Unit.cs`, `AAEmu.Game/Models/Game/Char/Character.cs`

---

### Refactoring / Рефакторинг

**refactor(skills): устранить замечания код-ревью позиционных скиллов** (c8e659f7d)

На русском:
- Убраны мёртвые проверки `caster.ObjId == target.ObjId` в Pos/BallisticPos/ArtilleryPos/RelativePos — условие никогда не выполнялось
- Debug-логирование и логика определения позиции корабля (гарпун) обёрнуты в `if (Logger.IsDebugEnabled)` — устраняет накладные расходы в production
- Заменена `$""` string interpolation в Debug-вызовах на `Logger.Debug(format, args)`
- Убраны неиспользуемые `case` из switch в `SetInitialTarget` (были no-op break)
- Упрощены фигурные скобки в case-блоках switch

English:
- Removed dead `caster.ObjId == target.ObjId` checks in Pos/BallisticPos/ArtilleryPos/RelativePos — condition never triggered
- Wrapped Logger.Debug calls and ship-position logic (harpoon) in `if (Logger.IsDebugEnabled)` — eliminates string interpolation overhead in production
- Replaced `$""` string interpolation in Debug calls with `Logger.Debug(format, args)`
- Removed unused no-op `case` entries from `SetInitialTarget` switch
- Simplified curly braces in switch case blocks

---

**fix(skills): улучшить обработку позиционных скиллов на кораблях** (2db63c9f4)

На русском:
- Сохранять `PositionTarget` в `SCSkillFiredPacket` для прицельных скиллов
- Исправить обработку RelativePos скиллов (гарпун и подобные)
- Добавить поддержку `SkillCastPosition2Target` и `SkillCastPosition3Target`
- Исправить вычисление координат для дудадов на кораблях

English:
- Save `PositionTarget` in `SCSkillFiredPacket` for aimed skills
- Fix RelativePos skill processing (harpoon and similar)
- Add support for `SkillCastPosition2Target` and `SkillCastPosition3Target`
- Fix coordinate calculation for doodads on ships

---

**fix(physics): исправить портирование кода корабельной физики из 1.2** (b898174d6)

На русском:
- Изменён TPS с 100 на 25 (читается из конфига `AppConfiguration`)
- Заменён `Thread.Sleep` в начале цикла на адаптивный sleep в конце
- Цикл физики изменён с accumulation (до 4 шагов) на single-step с фактическим elapsed delta
- Исправлен порядок операций: `ShoreResolve` до `ApplyForceAndTorque`, `SendMovementData` после всех взаимодействий
- Водный поток применяется как position offset (`RigidBody.Position +=`), а не как сила
- Водный поток применяется только когда корабль не на земле (`!GroundContactLatched`)
- Подключена интеграция `ShipHarpoonRopeController` в 5 точках: `CSStartSkillPacket`, `CSSkillControllerStatePacket`, `SlaveManager`, `CSUnhangPacket`, `PhysicsManager`
- Реализован полный `InitializeTerrain`: загрузка heightmap в физический мир
- Исправлен `RemoveShip`: деферится к physics thread через queue (thread-safe)
- Документация: Docs/ship-physics-port-fixes-2026-05-31.md

English:
- Changed TPS from 100 to 25 (read from `AppConfiguration`)
- Replaced unconditional `Thread.Sleep` at loop start with adaptive sleep at end
- Physics loop changed from accumulation (up to 4 steps) to single-step with actual elapsed delta
- Fixed operation order: `ShoreResolve` before `ApplyForceAndTorque`, `SendMovementData` after all interactions
- Water flow applied as position offset (`RigidBody.Position +=`) instead of force
- Water flow only applied when ship is not grounded (`!GroundContactLatched`)
- Integrated `ShipHarpoonRopeController` at 5 points: `CSStartSkillPacket`, `CSSkillControllerStatePacket`, `SlaveManager`, `CSUnhangPacket`, `PhysicsManager`
- Implemented full `InitializeTerrain`: heightmap loading into physics world
- Fixed `RemoveShip`: now deferred to physics thread via queue (thread-safe)
- Documentation: Docs/ship-physics-port-fixes-2026-05-31.md

---

## [5.0.7.0 private] - 30.05.2026

### Refactoring / Рефакторинг

**refactor(physics): заменить проверку IsStatic на MotionType в физических компонентах** (f53d15f05)

На русском:
- Обновлены пакеты NuGet (Jitter2, Microsoft.*, NLog, NLua) до последних версий
- Заменено использование свойства IsStatic на MotionType в файлах:
  - AAEmu.Game/Physics/Forces/Buoyancy.cs
  - AAEmu.Game/Physics/Forces/OneShotVelocityKick.cs
  - AAEmu.Game/Physics/ShipController.cs
  - AAEmu.Game/Physics/Terrain.cs

English:
- Updated NuGet packages (Jitter2, Microsoft.*, NLog, NLua) to latest versions
- Replaced IsStatic property usage with MotionType in files:
  - AAEmu.Game/Physics/Forces/Buoyancy.cs
  - AAEmu.Game/Physics/Forces/OneShotVelocityKick.cs
  - AAEmu.Game/Physics/ShipController.cs
  - AAEmu.Game/Physics/Terrain.cs

---

### Features / Особенности

**feat(physics): портировать полную морскую механику кораблей из develop 1.2** (6f19a1e45)

На русском:
- Физический движок Jitter2 с настраиваемым TPS (TargetPhysicsTps)
- Система ветра (Official/Realistic модели) для парусных кораблей
- Столкновения: корабль-берег, корабль-корабль, корабль-скалы, дудады, барьеры
- Гарпунная механика: канат, буксировка, разрыв от натяжения
- Визуальные эффекты: крен в поворотах, качание на волнах, наклон на мели
- CryEngine инфраструктура для BAI-файлов (Readers, Loaders, Mission)
- GM-команды: /shipbarrier, /waterdebug
- 52 юнит-теста для новой механики
- Полная документация на русском (Docs/ship-marine-mechanics.md)

English:
- Jitter2 physics engine with configurable TPS
- Wind system (Official/Realistic models) for sailing ships
- Collisions: ship-shore, ship-ship, ship-rocks, doodads, barriers
- Harpoon mechanics: rope, towing, tension breakage
- Visual effects: banking, wave rolling, shallow water tilt
- CryEngine infrastructure for BAI files (Readers, Loaders, Mission)
- GM commands: /shipbarrier, /waterdebug
- 52 unit tests for new mechanics
- Full Russian documentation (Docs/ship-marine-mechanics.md)

---

## [5.0.7.0 private] - 27.04.2026

### Bug Fixes / Исправления

**fix(баффы): исправить использование биотоплива транспорта** (37d72cb31)

На русском:
- Корректно обрабатывает BuffStackRule.Extend при повторном применении баффа
- Продлевает существующий эффект вместо создания нового
- Отсылает клиенту базовую длительность (BaseDuration) вместо суммарной
- Добавляет поле Buff.BaseDuration для хранения базовой длительности
- Исправляет Buffs.AddBuff для Extend: выбирает существующий бафф при MaxStack
- SQL-патч compact/2026-04-27 для скиллов 20291, 27032, 27446
- Регрессионные тесты: BuffStackingTests, BuffsStackingTests, SlaveMountSkillDataTests

English:
- Correctly handles BuffStackRule.Extend when reapplying buff
- Extends existing effect instead of creating new one
- Sends client base duration instead of total
- Adds Buff.BaseDuration field to store base duration
- Fixes Buffs.AddBuff for Extend: selects existing buff at MaxStack
- SQL patch compact/2026-04-27 for skills 20291, 27032, 27446
- Regression tests: BuffStackingTests, BuffsStackingTests, SlaveMountSkillDataTests

---

**fix(game): наследная последовательность воспроизводится только для игрока** (cdaef8d5a)

На русском:
- Исправлена проблема, когда наследная последовательность воспроизводилась неправильно

English:
- Fixed heir sequence playback logic

---

## [5.0.7.0 private] - 25.04.2026

### Bug Fixes / Исправления

**fix(skills): восстановить автоматический полив из цистерны** (e6b84866a)

На русском:
- Сопоставить SCSkillFired для mount-навыков с packet capture
- Использовать Unit caster, позиционную цель для скилла 15601
- Нулевые delay/channeling и repeat flag=2
- Добавить xUnit-покрытие и документацию по диагностике скилла 27202
- Документация: Docs/automatic-watering-skill-27202.md

English:
- Matched SCSkillFired for mount skills with packet capture
- Use Unit caster, positional target for skill 15601
- Zero delay/channeling and repeat flag=2
- Added xUnit coverage and documentation for skill 27202
- Documentation: Docs/automatic-watering-skill-27202.md

---

### Refactoring / Рефакторинг

**refactor: рефакторинг обработки навыков в CSStartSkillPacket** (c38a911b5)

На русском:
- Переработана логика обработки скиллов в пакете начала скилла
- Улучшена структура кода для лучшей читаемости

English:
- Reworked skill processing logic in skill start packet
- Improved code structure for better readability

---

## [5.0.7.0 private] - 24.04.2026

### Features / Особенности

**feat: Add SCPlaySequencePacket and play heir level up sequence** (4dda2581c)

На русском:
- Добавлен пакет SCPlaySequencePacket для воспроизведения анимационных последовательностей
- Реализовано воспроизведение последовательности повышения уровня наследника

English:
- Added SCPlaySequencePacket for playing animation sequences
- Implemented heir level up sequence playback

---

### Bug Fixes / Исправления

**fix: исправить DoodadFuncRatioChange, DoodadFuncRatioRespawn** (e24fae73f)

На русском:
- Исправлена логика изменения ratio для дудадов
- Исправлена логика перезапуска дудадов с учётом ratio

English:
- Fixed doodad ratio change logic
- Fixed doodad respawn logic with ratio consideration

---

**fix: Fix housing doodad restart placeholders** (701dd7137)

На русском:
- Исправлены плейсхолдеры перезапуска жилых дудадов

English:
- Fixed housing doodad restart placeholders

---

**fix: исправление вращения домов и реорганизация тестов** (c6a9cefe0)

На русском:
- Исправлена логика вращения домов
- Реорганизованы тесты для лучшей структуры

English:
- Fixed house rotation logic
- Reorganized tests for better structure

---

## [5.0.7.0 private] - 19.04.2026

### Bug Fixes / Исправления

**fix: Fix mail for tax (#38)**

На русском:
- Исправлена механика почты для налогов

English:
- Fixed mail mechanism for taxes

---

## Информация / Information

**Версия сервера / Server version:** 5.0.7.0 private
**Последнее обновление / Last updated:** 14.06.2026
**Ветка / Branch:** client_version/5.0_client_(2018-12-25)

---

## Commit History / История коммитов

### 2026-06-14

- **fe778e7a0** - test(pathfinding): добавить юнит-тесты для SpawnHeightDiagnostics, Building, Configs, AStarHeap, PathNode diagnostics
- **review(ai-behavior)** - refactor(ai-behavior): заменить хардкод MeleeAttackRange на Ai.Param, исправить SetWeaponRange (атакующий вместо цели), добавить safety clamp для melee без оружия, 100 юнит-тестов
- **fix(ai-behavior)** - fix(ai-behavior): добавить проверку CC-эффектов (Knockdown/Sleep/Stun) в PickSkillAndUseIt, убрать dead code (_isCrowdControlBlocked, UpdateCrowdControlState), добавить null-safe проверку IsDead
- **fix(appellation)** - fix(appellation): сделать buff_id серверно-авторитетным при смене звания, добавить SQL-миграцию, обновить юнит-тесты

### 2026-06-13

- **81aee6e20** - add(geo): Применить патч 6_AAEmu_pathfinding_V6_1_FULL by Guyldorum

### 2026-06-10

- **549f6e960** - fix(npc-movement): защитить Z-координату НПС при движении на поднятых площадках

### 2026-06-03

- **0a6030019** - fix(geodata): загружать statobjs.dat для правильного пути к CGF-моделям brush-объектов
- **75cb66fca** - feat(geodata): загрузка реальной CGF-сетки через CgfConverter для точного raycast полов
- **6c348e4c3** - fix(roaming): использовать улучшенный MoveTowards при роуминге NPC
- **59a486160** - fix(geodata): передавать текущую Z в GetHeight при корректировке высоты NPC
- **c7dd6718a** - feat(geodata): добавить статическую клиентскую геодату
- **bfb9d702e** - test(appellation): покрыть SCAppellationChangedPacket и CharacterAppellations.Change тестами
- **1aa349e50** - fix(appellation): передавать buffId звания в пакет SCAppellationChanged by BwonSamdi

### 2026-06-02

- **e41f21200** - fix(physics): исправить видимый «прыжок» вниз сразу после появления кораблей by Master-Veles

### 2026-05-31

- **f23e99d8f** - fix(ship-breathing): не проверять дыхание персонажа на палубе корабля
- **8fa9a82fb** - docs: обновить CHANGELOG коммитами от 31.05.2026
- **c8e659f7d** - refactor(skills): устранить замечания код-ревью позиционных скиллов
- **2db63c9f4** - fix(skills): улучшить обработку позиционных скиллов на кораблях
- **b898174d6** - fix(physics): исправить портирование кода корабельной физики из 1.2

### 2026-05-30

- **042df0a73** - docs: обновить CHANGELOG с информацией о рефакторинге физических компонентов
- **f53d15f05** - refactor: заменить проверку IsStatic на MotionType в физических компонентах
- **a90c5b521** - add: добавить CHANGELOG.md
- **6f19a1e45** - feat(physics): портировать полную морскую механику кораблей из develop 1.2

### 2026-04-27

- **37d72cb31** - fix(баффы): исправить использование биотоплива транспорта
- **cdaef8d5a** - fix(game): наследная последовательность воспроизводится только для игрока

### 2026-04-25

- **e6b84866a** - fix(skills): восстановить автоматический полив из цистерны
- **c38a911b5** - refactor: рефакторинг обработки навыков в CSStartSkillPacket

### 2026-04-24

- **4dda2581c** - Add SCPlaySequencePacket and play heir level up sequence
- **e24fae73f** - fix: исправить DoodadFuncRatioChange, DoodadFuncRatioRespawn
- **701dd7137** - Fix housing doodad restart placeholders
- **c6a9cefe0** - fix: исправление вращения домов и реорганизация тестов

### 2026-04-19

- **0850991d1** - Fix mail for tax (#38)
