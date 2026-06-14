# Code Review: AI Behavior Refactor & Unit Test Coverage

**Date:** 2026-06-14
**Version:** AAEmu 5.0.7.0 private
**Reviewer:** Senior Developer Code Review
**Branch:** `client_version/5.0_client_(2018-12-25)`

---

## 1. Scope of Changes

### 1.1. Modified Files

| File | Change Type | Description |
|------|-------------|-------------|
| `AAEmu.Game/Models/Game/AI/v2/Framework/Behavior.cs` | Modified | Replaced hardcoded `MeleeAttackRange` constant with configurable `Ai.Param.MeleeAttackRange`; fixed `SetWeaponRange` to use attacker equipment instead of target; added `FlyingHeightGapMultiplier` constant for vertical detection |
| `AAEmu.Game/Models/Game/AI/v2/Behaviors/WildBoar/WildBoarAttackBehavior.cs` | Modified | Updated `SetWeaponRange` call to new signature (removed `target` parameter) |
| `AAEmu.Game/Models/Game/Skills/Skill.cs` | Modified | Removed redundant melee range override in `CalculateRange`; added safety clamp for melee skills without weapon slot; fixed indentation in `ApplyEffects` and reagent checking blocks |
| `Docs/CHANGELOG.md` | Modified | Added test documentation entries and commit history |

### 1.2. New Files (Unit Tests)

| File | Tests | Coverage Area |
|------|-------|---------------|
| `AAEmu.UnitTests/Game/Core/Managers/SpawnHeightDiagnosticsTests.cs` | 17 | SpawnHeightDiagnostics singleton: Record/TryGet/Remove/CleanupOldEntries/Clear, Entry properties, EntryTtl |
| `AAEmu.UnitTests/Game/Models/Game/Ai/AStar/AStarHeapTests.cs` | 16 | AStarHeap: Push/Pop, ordering (F→H→Seq), dynamic resizing, AStarEntry struct |
| `AAEmu.UnitTests/Game/Models/Game/Ai/AStar/PathNodeDiagnosticsTests.cs` | 12 | PathNode diagnostic fields, PathStatus enum, EstimateFullPathLength |
| `AAEmu.UnitTests/Game/Models/Game/NPChar/BuildingTests.cs` | 14 | Building.GetFloorHeight by index and by reference Z, edge cases |
| `AAEmu.UnitTests/Game/Models/Game/PathingConfigTests.cs` | 20 | PathingConfig defaults and setters |
| `AAEmu.UnitTests/Game/Models/Game/SpawnHeightConfigTests.cs` | 15 | SpawnHeightConfig defaults and setters |
| `AAEmu.UnitTests/Game/Models/Game/WorldConfigPathingTests.cs` | 6 | WorldConfig.SpawnHeight and WorldConfig.Pathing defaults and assignment |

**Total new tests: 100**

---

## 2. Findings and Fixes Applied

### 2.1. WARNING — Removed Melee Range Clamp in `CalculateRange`

**File:** `AAEmu.Game/Models/Game/Skills/Skill.cs`

**Problem:** The old `CalculateRange` method had a special case for melee base skills (ID 2, 3, 4) that clamped `skillRange` to 4f when the template value was 0 or unreasonably large (>5m). The refactoring removed this clamp, relying on weapon slot logic. However, when `WeaponSlotForRangeId` is 0 (no weapon configured), `maxRangeCheck` retained the raw template value (e.g., 25f), allowing melee attacks at unrealistic distances.

**Fix applied:** Added a safety clamp after the weapon slot block:
```csharp
else if (template.Id == 2 || template.Id == 3 || template.Id == 4)
{
    // Safety clamp for melee base skills without a weapon slot configured.
    if (maxRangeCheck > 5.0)
        maxRangeCheck = 4f;
}
```

Also simplified the redundant `if/else` at the top of `CalculateRange` — the old code had separate branches for melee/non-melee with identical `skillRange == 0` fallback. Now a single check.

### 2.2. SUGGESTION — Semantic Mismatch: Flying Height Gap Using MeleeAttackRange

**File:** `AAEmu.Game/Models/Game/AI/v2/Framework/Behavior.cs`

**Problem:** The `maxHeightGap` calculation for flying NPCs in `CheckAlert` used `Ai.Param.MeleeAttackRange` (default 4f). While numerically identical to the old hardcoded constant, the vertical detection height is conceptually unrelated to melee attack range. If a server admin increases `MeleeAttackRange` for a flying creature, it would unintentionally increase the vertical detection window.

**Fix applied:** Introduced a dedicated constant with documentation:
```csharp
/// <summary>
/// Multiplier for flying NPC vertical height gap detection.
/// Not related to melee attack range — kept separate so that tuning MeleeAttackRange
/// does not unintentionally affect vertical detection.
/// </summary>
private const float FlyingHeightGapMultiplier = 3.5f;
```

### 2.3. SUGGESTION — Misleading Test Name

**File:** `AAEmu.UnitTests/Game/Core/Managers/SpawnHeightDiagnosticsTests.cs`

**Problem:** Test `EntryTtl_Default_Is30Minutes` claimed to verify the default TTL is 30 minutes, but since `SpawnHeightDiagnostics` is a singleton and the constructor sets `EntryTtl = 50ms`, the test only verified the 50ms value.

**Fix applied:** Renamed to `EntryTtl_CanBeSetAndRead`, added try/finally to save/restore original value, and tests multiple set/get cycles.

---

## 3. Positive Observations

### 3.1. `SetWeaponRange` Bug Fix

The old code passed `target` to `SetWeaponRange` and used the **target's** equipment to calculate weapon range. This was clearly wrong — the attacker's weapon should determine the attack range. The refactoring correctly changed this to use `Ai.Owner` (the attacker).

### 3.2. Configurable Melee Attack Range

Replacing the hardcoded `private const float MeleeAttackRange = 4f` with `Ai.Param.MeleeAttackRange` (default 4f in `AiParams.cs`) is a significant improvement. Server admins can now configure per-NPC-type melee attack range via the `data.meleeAttackRange` JSON field, enabling fine-tuning without code changes.

### 3.3. Comprehensive Unit Test Coverage

The 100 new tests cover critical subsystems that previously had zero test coverage:
- **AStarHeap** — validates heap ordering invariants (F→H→Seq), dynamic resizing, and struct semantics
- **SpawnHeightDiagnostics** — covers the singleton lifecycle, TTL-based cleanup, and edge cases
- **Building.GetFloorHeight** — validates floor resolution logic for multi-story structures
- **Config tests** — verify that all configuration defaults match expected values and that properties are settable, catching accidental regressions

### 3.4. Code Cleanup

The whitespace/indentation fixes in `Skill.cs` (inconsistent `else` nesting in `ApplyEffects` and reagent checking) improve readability and reduce cognitive load.

---

## 4. Remaining Observations (Not Blocking)

### 4.1. `_minWeaponRange` / `_maxWeaponRange` Fields Appear Unused

In `Behavior.cs`, `SetWeaponRange` stores results in `_minWeaponRange` and `_maxWeaponRange` fields, but no code in the current codebase reads these fields. They may be intended for future use or left over from a previous refactor. Consider either removing them or documenting their intended purpose.

### 4.2. `Thread.Sleep` in Tests

`SpawnHeightDiagnosticsTests.cs` uses `Thread.Sleep(50)` and `Thread.Sleep(150)` to test TTL-based expiration. While functional, this makes tests slow and fragile on loaded CI systems. Consider using a time-provider abstraction or `Task.Delay` with a mockable clock for faster, more reliable tests.

### 4.3. Singleton Test Isolation

`SpawnHeightDiagnostics` is a singleton, and tests mutate its state (`Clear()`, `EntryTtl = ...`). The test constructor resets state, but parallel test execution could cause flaky failures. Consider adding a reset mechanism or using `[Collection]` to prevent parallel execution.

---

## 5. Files Changed Summary

```
Modified:
  AAEmu.Game/Models/Game/AI/v2/Framework/Behavior.cs
  AAEmu.Game/Models/Game/AI/v2/Behaviors/WildBoar/WildBoarAttackBehavior.cs
  AAEmu.Game/Models/Game/Skills/Skill.cs
  Docs/CHANGELOG.md

Added:
  AAEmu.UnitTests/Game/Core/Managers/SpawnHeightDiagnosticsTests.cs
  AAEmu.UnitTests/Game/Models/Game/Ai/AStar/AStarHeapTests.cs
  AAEmu.UnitTests/Game/Models/Game/Ai/AStar/PathNodeDiagnosticsTests.cs
  AAEmu.UnitTests/Game/Models/Game/NPChar/BuildingTests.cs
  AAEmu.UnitTests/Game/Models/Game/PathingConfigTests.cs
  AAEmu.UnitTests/Game/Models/Game/SpawnHeightConfigTests.cs
  AAEmu.UnitTests/Game/Models/Game/WorldConfigPathingTests.cs
```

---

## 6. Verdict

**APPROVED** — after applying the three fixes described above.

The changes improve configurability (configurable melee range), fix a real bug (weapon range using wrong unit's equipment), add comprehensive test coverage (100 tests across 6 new test files), and clean up code quality (indentation, removed redundant branches). The fixes address the only warning-level issue (removed safety clamp) and two suggestions for clarity.
