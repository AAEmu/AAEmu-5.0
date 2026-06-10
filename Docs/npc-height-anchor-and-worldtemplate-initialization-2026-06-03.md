# Исправление высоты спавна НПС и инициализации WorldTemplate

## Обзор

Эти изменения исправляют критические проблемы с высотой рельефа при спавне НПС и инициализации геоданных:

1. **WorldTemplate не создавался при загрузке мира** — всегда возвращал `null`
2. **Heightmap использовалась некорректно** — методы читали из несуществующих файлов `heightmap.dat`
3. **Граничные проверки в WorldTemplate** вызывали `IndexOutOfRangeException`

## Проблемы и решения

### 1. WorldTemplate не инициализировался

**Проблема:**
- `World.Template` никогда не присваивался → всегда `null`
- `IngestObjectDatIntoStaticGeometryIndex` пропускал все миры
- `StaticGeometryIndex` всегда оставался пустым
- `TryRaycastDown` не находил полы зданий → НПС не получали якорь `InteriorFloor`

**Решение:**
В `WorldManager.Load()` после создания `World` создаётся `WorldTemplate` с нужными полями, массив `WorldCell[CellX, CellY]` заполняется экземплярами (данные загружаются лениво при первом обращении), и присваивается `world.Template`.

### 2. Heightmap использовалась некорректно

**Проблема:**
- `NpcSpawnHeightOverrides.GetSpawnHeight` использовал `world.Template?.GetHeight()`
- Этот метод использует `WorldCell.HeightMap`, загружаемый из per-cell `heightmap.dat`
- Если эти файлы недоступны, возвращается 0, что нарушало логику якоря

**Решение:**
Заменено на `world.GetHeight(x, y)` — использует `World.HeightMaps`, загружаемый глобально при старте сервера.

### 3. Граничные проверки в WorldTemplate

**Проблема:**
- Проверки `> CellX/CellY` допускали выход за границы массива
- `IndexOutOfRangeException` в `GetBaiByPos` и `GetRawHeightMapHeight`
- Краш происходил каждый тик `MoveTowards`

**Решение:**
Граничные проверки изменены с `> CellX/CellY` на `>= CellX/CellY`, добавлена проверка bounds в `GetBaiByPos`.

## Изменённые файлы

- `AAEmu.Game/Core/Managers/World/WorldManager.cs`
- `AAEmu.Game/Models/Game/NPChar/NpcSpawnHeightOverrides.cs`
- `AAEmu.Game/Physics/WorldStaticGeometryIndex.cs`
- `AAEmu.Game/Models/Game/World/WorldTemplate.cs`

## Тестирование

Для проверки изменений добавлены следующие тесты:

- `WorldTemplate_GetCell_Out_ofBounds_ReturnsNull` — проверка граничных условий
- `WorldTemplate_GetHeight_Out_ofBounds_ReturnsZero` — проверка выхода за границы
- `NpcSpawnHeightOverrides_GetSpawnHeight_UsesWorldGetHeight` — проверка использования глобальной heightmap

## Связанные документы

- [Static Geodata Implementation](static-geodata-implementation.md)
- [Ship Physics Port Fixes](ship-physics-port-fixes-2026-05-31.md)