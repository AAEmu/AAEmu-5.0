# CHANGELOG - AAEmu 5.0.7.0 Private Server

## [5.0.7.0 private] - 31.05.2026

### Refactoring / Рефакторинг

**refactor(skills): устранить замечания код-ревью позиционных скиллов**

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

### Bug Fixes / Исправления

**fix(skills): улучшить обработку позиционных скиллов на кораблях**

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

**fix(physics): исправить портирование кода корабельной физики из 1.2**

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

**refactor(physics): заменить проверку IsStatic на MotionType в физических компонентах**

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

## [5.0.7.0 private] - 30.05.2026

### Features / Особенности

**feat(physics): портировать полную морскую механику кораблей из develop 1.2**

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

**fix(баффы): исправить использование биотоплива транспорта**

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

## [5.0.7.0 private] - 25.04.2026

### Bug Fixes / Исправления

**fix(skills): восстановить автоматический полив из цистерны**

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

**refactor: рефакторинг обработки навыков в CSStartSkillPacket**

На русском:
- Переработана логика обработки скиллов в пакете начала скилла
- Улучшена структура кода для лучшей читаемости

English:
- Reworked skill processing logic in skill start packet
- Improved code structure for better readability

---

## [5.0.7.0 private] - 24.04.2026

### Features / Особенности

**feat: Add SCPlaySequencePacket and play heir level up sequence**

На русском:
- Добавлен пакет SCPlaySequencePacket для воспроизведения анимационных последовательностей
- Реализовано воспроизведение последовательности повышения уровня наследника

English:
- Added SCPlaySequencePacket for playing animation sequences
- Implemented heir level up sequence playback

---

### Bug Fixes / Исправления

**fix: исправить DoodadFuncRatioChange, DoodadFuncRatioRespawn**

На русском:
- Исправлена логика изменения ratio для дудадов
- Исправлена логика перезапуска дудадов с учётом ratio

English:
- Fixed doodad ratio change logic
- Fixed doodad respawn logic with ratio consideration

---

**fix: Fix housing doodad restart placeholders**

На русском:
- Исправлены плейсхолдеры перезапуска жилых дудадов

English:
- Fixed housing doodad restart placeholders

---

**fix: исправление вращения домов и реорганизация тестов**

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
**Последнее обновление / Last updated:** 31.05.2026  
**Ветка / Branch:** client_version/5.0_client_(2018-12-25)

---

## Commit History / История коммитов

### 413bb59c3 - docs: обновить CHANGELOG с информацией о рефакторинге физических компонентов

**refactor(physics): заменить проверку IsStatic на MotionType в физических компонентах**

- Обновлены пакеты NuGet (Jitter2, Microsoft.*, NLog, NLua) до последних версий
- Заменено использование свойства IsStatic на MotionType в файлах физики

### f53d15f05 - refactor: заменить проверку IsStatic на MotionType в физических компонентах

- Обновлены пакеты NuGet (Jitter2, Microsoft.*, NLog, NLua) до последних версий
- Заменено использование свойства IsStatic на MotionType в файлах:
  - AAEmu.Game/Physics/Forces/Buoyancy.cs
  - AAEmu.Game/Physics/Forces/OneShotVelocityKick.cs
  - AAEmu.Game/Physics/ShipController.cs
  - AAEmu.Game/Physics/Terrain.cs
