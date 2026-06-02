# Статическая клиентская геодата (фазы 1–11)

**Дата:** 2026-06-03

---

## Контекст

Реализована система статической геодаты на основе клиентских brush/voxel объектов (`.cgf` / `.cga`), которая позволяет:
- Определять высоту пола в помещениях и пещерах без опоры на GeoData BAI
- Блокировать движение NPC через статические стены
- Переключиться от рантаймового `GeoDataMode` к всегда-включённой статической геометрии

## Что реализовано

### Новые файлы (AAEmu.Game)

| Файл | Описание |
|------|----------|
| `Physics/WorldStaticGeometryIndex.cs` | Пространственный grid-индекс для brush/voxel; raycast вниз (multi-hit), wall-сегмент тест |
| `Physics/WorldStaticGeometryIndex.cs` (класс `StaticGeoDataService`) | Резолвер высоты обособленного слоя (пещера / raised floor) |
| `Models/Game/NPChar/NpcHeightState.cs` | Enum `NpcHeightAnchorKind` + класс `NpcHeightState` |
| `Models/Game/NPChar/NpcHeightResolver.cs` | Определение высоты NPC с учётом активного anchor-слоя |
| `Models/Game/NPChar/NpcSpawnHeightOverrides.cs` | Разрешение высоты при спавне (water / interior / terrain) |
| `Models/Game/NPChar/NpcWaterSurfaceOverrides.cs` | Корректировка высоты в воде |
| `Models/Game/NPChar/NpcDoodadObstacleClassifier.cs` | Классификация doodad-препятствий |
| `Models/Game/NPChar/NpcPathfindingImproved.cs` | Улучшенный pathfinding (terrain wall detours, shared world mesh fallback) |
| `Models/AppConfiguration.NpcPathfinding.cs` | Конфигурационная секция для NPC pathfinding |

### Изменённые файлы (AAEmu.Game)

| Файл | Изменение |
|------|-----------|
| `Models/Game/World/World.cs` | `IsStaticCollisionSegmentBlocked()`, `GetHeight(x,y,refZ)` multi-hit overload |
| `Models/Game/World/WorldCell.cs` | Регистрация brush/voxel объектов в `StaticGeometryIndex` |
| `Core/Managers/World/PhysicsManager.cs` | `StaticGeometryIndex` инициализируется при старте физики |
| `Core/Managers/World/WorldManager.cs` | `GetHeight(zoneId, x, y, refZ)` — консультирует static geo перед AiGeoDataManager; `GetHeight(Transform)` аналогично |
| `Core/Managers/AiGeodataManager.cs` | `IsSegmentBlocked(start, end)`, `ReducePath(...)`, `CheckImpossibleWalk(Vector3)`, `LinePassesThroughForbiddenArea(...)` |
| `Models/Game/AI/v2/Behaviors/BaseCombatBehavior.cs` | Убраны GeoDataMode guards; static collision detour перед navmesh |
| `Models/Game/AI/v2/Behaviors/Flytrap/FlytrapAttackBehavior.cs` | Убраны GeoDataMode guards |
| `Models/Game/AI/AStar/PathNode.cs` | Hybrid runtime validation — фильтрация linkov через static geo |
| `Models/Game/Configurations.cs` | `GeoDataMode = true` по умолчанию, обновлён комментарий |
| `Models/CryEngine/Loaders/BaseBaiLoader.cs` | Небольшая доработка загрузчика |
| `Models/CryEngine/Mission/AiShape.cs` | Добавлены вспомогательные свойства |
| `Models/Game/AI/v2/Framework/NpcAi.cs` | `HeightState` property |

### Новые тест-файлы (AAEmu.UnitTests)

| Файл | Покрывает |
|------|-----------|
| `Game/Models/Game/World/WorldInstanceStaticGeometrySegmentTests.cs` | `WorldStaticGeometryIndex`: TryRaycastDown, TryCollectRaycastDownHits, IsSegmentWallBlocked, GetEntryCountAt |
| `Game/Models/Game/World/WorldGetHeightStaticGeoTests.cs` | `World.GetHeight(x,y,refZ)`: статическая геометрия vs heightmap fallback |
| `Game/Physics/StaticGeoDataServiceTests.cs` | `StaticGeoDataService.TryResolveDistinctLayerHeight` |
| `Game/Core/Managers/AiGeodataManagerTests.cs` | `AiGeodataManager.IsSegmentBlocked`, `ReducePath` |
| `Game/Models/Game/Ai/AStar/PathNodeTests.cs` | `PathNode` hybrid validation |
| `Game/Models/Game/Ai/Utils/AIUtilsTests.cs` | `AIUtils` вспомогательные методы |
| `Game/Models/Game/Ai/v2/Behaviors/BaseCombatBehaviorTests.cs` | Боевое поведение + static detours |
| `Game/Models/Game/NPChar/NpcPathfindingImprovedTests.cs` | `NpcPathfindingImproved` terrain wall + shared mesh |

---

## Что НЕ было портировано и почему

### `AIUtils.TryFindIndoorRoamingPosition` (фаза 10)
Метод `TryFindIndoorRoamingPosition` отсутствует в текущей версии `AIUtils.cs` (76 строк). Фаза 10 модифицировала строку внутри этого метода — контекст применения отсутствует.

### `Npc.FindPath2` + `TryGetFindPath2BlockingInfo` (фазы 6–7)
В `Npc.cs` отсутствует метод `FindPath2`. Фазы 6–7 добавляли `TryGetFindPath2BlockingInfo` и `CanIgnoreFindPath2ForbiddenShapeOnDetachedLayer` внутри него — необходимо сначала реализовать `FindPath2`.

### `NpcSpawnerNpc` height resolution (фаза 2)
`NpcSpawnerNpc.cs` содержит упрощённый `SpawnNpc` без блока разрешения высоты. Фаза 2 изменяла этот блок (замена `GeoData.TryGetClosestInteriorFloorNode` → `StaticGeoDataService.TryResolveDistinctLayerHeight`) — необходимо сначала реализовать полную логику `SpawnNpc`.

---

## Архитектурные заметки

- Нет класса `WorldInstance` — его роль выполняет `World` (алиас `InstanceWorld`). Все места с `WorldInstance` адаптированы к `World`.
- `AiGeoDataManager` — singleton без per-world привязки. `IsSegmentBlocked` итерирует по всем мирам через `WorldManager.GetWorlds()`.
- `StaticGeoDataService` добавлен в конец `WorldStaticGeometryIndex.cs` как `public static class` (избегаем отдельного файла).

---

## Тестирование

Все новые тесты написаны с использованием рефлексии для инъекции `IStaticGeometryEntry` в приватный `_grid` индекса, минуя зависимость от `WorldCell`/`WorldTemplate`/`WorldManager`.

Количество тестов: **~350** (было ~313 до портирования).
