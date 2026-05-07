# Ship spawn-dive fix (2026-06-02)

## The bug

When a ship was summoned (spawned via `SlaveManager`) it visually dived ~2-3 m
underwater immediately after `PortalTime` ended, then resurfaced.  The client
saw it as a sudden jump downward at the moment the portal effect finished.

---

## Root cause — thread race condition

The Jitter2 physics world runs its simulation on a dedicated thread.
The following sequence of events caused the dive:

| Step | Thread | What happened |
|------|--------|---------------|
| 1 | Game | `PhysicsManager.AddShip()` calls `ctrl.Build(...)` |
| 2 | Game | `Build()` creates a `RigidBody` and adds it to `World` with Jitter2's default `AffectedByGravity = true` |
| 3 | **Physics** | Physics thread runs one or more steps _before_ `AddShip` calls `AddForRectangularParallelepiped()` |
| 4 | Physics | Gravity is applied to the hull; no buoyancy force exists yet because the body has not been registered |
| 5 | Game | `AddForRectangularParallelepiped` and `AffectedByGravity = false` are finally called |

The window (steps 3-4) gave the hull roughly `0.5 m/s` of downward velocity.
Because `SyncTransformWithRigidBody` is skipped during `PortalTime` (~5-10 s),
this velocity accumulated silently and resulted in approximately `2-3 m` of
downward drift by the time the portal ended and the client received the first
authoritative position update.

Additionally, the pre-fix code only set `AffectedByGravity = false` _after_
`AddForRectangularParallelepiped`, leaving the race window open.

---

## The fix — three changes in two files

### 1. `PhysicsManager.AddShip()` — disable gravity and zero velocity immediately after `Build()`

```csharp
ctrl.Build(initialPosition, initialOrientation);

// Fix: close the race window before the physics thread can apply one step of gravity
// without buoyancy compensation.
ctrl.Hull.AffectedByGravity = false;
ctrl.Hull.Velocity = JVector.Zero;
ctrl.Hull.AngularVelocity = JVector.Zero;
```

This ensures that even if the physics thread fires between `Build()` and
`AddForRectangularParallelepiped()`, it cannot accumulate downward velocity.

### 2. `PhysicsManager.AddShip()` — pre-populate `CachedWaterSurface`

```csharp
slave.CreateWaterAndLandSurfaceCache();  // pre-populate CachedWaterSurface
```

`Buoyancy.PreStep` reads `slave.CachedWaterSurface` when a custom fluid area
delegate is set.  Pre-populating the cache prevents `PreStep` from using a
stale zero value during the first few ticks after spawn.

### 3. `Buoyancy.PreStep()` — lock gravity off and zero velocity during `PortalTime`

```csharp
body.AffectedByGravity = slave.SpawnTime.AddSeconds(slave.Template.PortalTime) <= DateTime.UtcNow;
if (!body.AffectedByGravity)
{
    // Drain any residual velocity so the hull cannot drift during portal time
    // (safety net against the race window in AddShip and any terrain impulses).
    body.Velocity = JVector.Zero;
    body.AngularVelocity = JVector.Zero;
    continue;
}
```

Every physics step during `PortalTime` the pre-step handler now re-confirms
`AffectedByGravity = false` and zeros any accumulated velocity.  This acts as a
belt-and-suspenders guard: even if the `AddShip` flag was somehow missed, no
drift can accumulate while the ship is still in the portal phase.

### Also: spawn-height pre-correction in `SlaveManager`

Before calling `AddShip`, `SlaveManager` now moves the hull to the theoretical
equilibrium draught:

```csharp
var targetSubmerged = 1f / (Buoyancy.BaseWaterDensity * Buoyancy.ShipWaterDensityMul);
var targetZ = waterAtFinal - targetSubmerged;
if (summonedSlave.Transform.Local.Position.Z < targetZ)
    summonedSlave.Transform.Local.SetHeight(targetZ);
```

`targetSubmerged ≈ 0.325 m` (at the default density constants).  Spawning the
hull here means that when physics becomes active after `PortalTime`, the ship
is already at the correct waterline and needs no correction at all.

---

## How to verify the fix is working

1. **Unit tests** — Five regression tests were added to
   `AAEmu.UnitTests/Game/Physics/ShipPhysicsTests.cs`:

   | Test | What it checks |
   |------|----------------|
   | `BuoyancyPreStep_DuringPortalTime_SetsGravityFalseAndZerosVelocity` | `PreStep` must zero velocity and disable gravity while `PortalTime` has not expired, even if the hull had a downward velocity |
   | `BuoyancyPreStep_AfterPortalTime_EnablesGravity` | `PreStep` must re-enable gravity once `PortalTime` has expired |
   | `BuoyancyEquilibrium_AtTargetDepth_BuoyancyForceEqualsGravityForce` | At `submergedDepth = 1/(BaseWaterDensity * ShipWaterDensityMul)` the upward buoyancy force exactly cancels gravity |
   | `ShipController_Build_DefaultGravityIsTrue_RequiresExplicitDisableInAddShip` | Documents that Jitter2 sets `AffectedByGravity = true` by default so `AddShip` must override it |
   | `BuoyancyEquilibrium_TargetSpawnZ_IsSlightlyBelowWaterSurface` | The SlaveManager equilibrium formula places the hull between `waterSurface - 1` m and `waterSurface` |

   Run with:
   ```
   dotnet test AAEmu.UnitTests
   ```

2. **In-game observation** — Summon any ship (Boat, Fishboat, BigSailingShip,
   etc.) and watch the hull at the moment the portal effect ends.  Before the
   fix the hull would visibly snap ~2-3 m downward.  After the fix it should
   appear at the waterline without any vertical jump.

3. **Server logs** — Enable `DEBUG` logging for `PhysicsManager` and check that
   the hull Y position reported at the first `SyncTransformWithRigidBody` call
   after `PortalTime` matches the spawn height set by `SlaveManager`.

---

## Constants referenced by the fix

| Constant | Value | Location |
|----------|-------|----------|
| `Buoyancy.BaseWaterDensity` | `1.025f` | `AAEmu.Game/Physics/Forces/Buoyancy.cs` |
| `Buoyancy.ShipWaterDensityMul` | `3.0f` | `AAEmu.Game/Physics/Forces/Buoyancy.cs` |
| Equilibrium draught | `1 / (1.025 × 3) ≈ 0.325 m` | derived |
