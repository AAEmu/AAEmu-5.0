# Ship Physics Port Fixes (2026-05-31)

## Overview

Port from `aaemu_nikes_1.2 (Develop)` starting with commit `d82571138fd562fdbe18619240e71bbb77682a4e` had several integration issues. While the core physics files (ShipController, ShipShoreInteraction, Buoyancy, ShipHarpoonTowPhysics, ShipHarpoonRopeController, ShipHarpoonRopeState) were ported correctly, the **glue code** in PhysicsManager and packet handlers was incomplete.

## Issues Found and Fixed

### 1. Slow/Frozen Ship Steering

**Root Cause**: Multiple issues in `PhysicsManager.cs`:

| Problem | Effect | Fix |
|---------|--------|-----|
| `Thread.Sleep(fixedStep)` at top of loop (unconditional 10ms) | Guaranteed 10ms+ input latency every tick | Changed to adaptive sleep at end of loop (sleep only remaining budget) |
| 100 TPS with fixed-step accumulation (up to 4 steps/frame) | Input read AFTER all physics steps; old input used for 4 steps | Single step per frame with actual elapsed delta (25 TPS like reference) |
| Water flow applied as `AddForce(mass * 3.15 * flow)` | Massive force overwhelms throttle in current areas | Changed to position offset (`RigidBody.Position += flow * dt`), only when not grounded |
| Water depth probe at fixed `Position + UnitZ * -2f` | Incorrect depth detection for small/large ships | Uses `ShipModel.MassBoxSizeZ / -2 * scale` (ship-relative keel depth) |
| No grounded check for water flow | Current applied to beached ships, fights ground friction | Only applies flow when `!GroundContactLatched && floor < waterSurface` |
| Water/land cache refreshed every tick | Wasted CPU at high TPS; possible stale data race | Throttled: refresh every 2+ loops or when moved > 1 unit |

**File**: `AAEmu.Game/Core/Managers/World/PhysicsManager.cs`

### 2. Collision Appears to Work Only from Hull Center

**Root Cause**: `SendUpdatedMovementData` was called BEFORE collision resolution.

| Problem | Effect | Fix |
|---------|--------|-----|
| Packet sent before shore/ship/cliff interactions resolve | Client sees raw physics position, correction applied next tick | Moved `SendUpdatedMovementData` to AFTER all interaction resolution |
| Shore resolve was batch (`ResolveAll`) after force application | GroundContactLatched stale during force application | Now per-ship `ResolveTerrainContacts` BEFORE `ApplyForceAndTorque` |

**Note**: The actual probe code and MassBoxSizeY values are correct. The "center only" appearance was because the client received uncorrected positions and the correction came one frame late (causing visible "snap" at bow/stern but smooth "push" at center).

### 3. Harpoon Not Working (No Visible Rope/Arrow)

**Root Cause**: 5 critical integration points were completely missing.

| Integration Point | File | What Was Missing |
|------------------|------|-----------------|
| **P0**: Rope launch trigger | `CSStartSkillPacket.cs` | `ShipHarpoonRopeController.OnLaunchSucceeded()` never called after harpoon skill (13749) succeeds |
| **P0**: Client rope sync | `CSSkillControllerStatePacket.cs` | `TryApplySkillControllerState()` never called; packet data discarded with only a log |
| **P1**: Operator exit | `SlaveManager.cs` | `OnOperatorLeftSlave()` never called in `UnbindSlave` |
| **P1**: Unhang rope break | `CSUnhangPacket.cs` | `BreakRopeForClients()` never called on unhang |
| **P2**: Physics tick integration | `PhysicsManager.cs` | `TickTensionTearAndGetHullRecoilDeltaV`, `TickHarpoonRopeControllerLifetime`, `ApplyShipPairHarpoonTowImpulses` never called |

The rope visual is triggered by `SCSkillControllerStatePacket` sent to clients via `BroadcastSkillControllerRopeState()`. Without `OnLaunchSucceeded`, this broadcast never fires.

### 4. Ship Dives on Spawn (Submerges and Resurfaces)

**Root Cause**: Multi-step physics accumulation loop caused burst gravity application.

| Problem | Effect | Fix |
|---------|--------|-----|
| Accumulation loop (up to 4 steps of 10ms) on first post-PortalTime tick | 4 gravity steps with no game logic response = rapid downward acceleration | Single step with actual delta (max ~40ms); buoyancy and game logic run every step |

**Additional mitigation**: The reference runs at 25 TPS (40ms per frame). Larger dt per step means buoyancy force is stronger per step (it's proportional to submerged depth which increases faster per-step with larger dt), leading to faster equilibrium.

### 5. RemoveShip Thread Safety

**Root Cause**: `RemoveShip` called `_physWorld.Remove()` directly from caller thread while physics thread could be stepping.

| Problem | Effect | Fix |
|---------|--------|-----|
| Direct `_physWorld.Remove(body)` from game thread | Race condition -> `AccessViolationException` | Deferred to physics thread via `_pendingActions` queue (matching reference) |

## Architecture After Fix

### Physics Loop Order (matches reference):
```
1. Adaptive sleep (remaining time budget)
2. Process pending add/remove actions
3. Take body snapshot
4. Single physics world step (actual elapsed delta)
5. Per-ship:
   a. Throttled water/land cache refresh
   b. SyncTransformWithRigidBody
   c. BoatPhysicsTick (input smoothing, land physics)
   d. Harpoon tension tear + recoil
   e. ResolveTerrainContacts (shore/ground probe, latch, penetration resolve)
   f. Water flow as position offset (only when floating)
6. Harpoon rope controller lifetime expiry (all ships)
7. ApplyForceAndTorque (all ships)
8. Ship-to-ship collision
9. Ship-doodad collision
10. Ship-barrier collision
11. Ship-cliff collision
12. Ship-pair harpoon tow impulses
13. Damage accumulators
14. SendUpdatedMovementData (all ships)
```

### Harpoon Flow:
```
Client fires harpoon skill (13749)
  -> CSStartSkillPacket -> HandleMountedSkill -> skill.Use succeeds
  -> ShipHarpoonRopeController.OnLaunchSucceeded(slave, target, operator)
    -> Sets HarpoonRope.IsEngaged = true
    -> BroadcastSkillControllerRopeState -> SCSkillControllerStatePacket to all nearby clients
    -> Client renders rope visual

Client syncs rope length:
  -> CSSkillControllerStatePacket -> TryApplySkillControllerState
    -> Updates RopeLength, broadcasts to other clients

Physics tick:
  -> TickTensionTearAndGetHullRecoilDeltaV (checks stretch/jerk, may tear rope)
  -> ApplyShipPairHarpoonTowImpulses (tow force between harpoon pairs)
  -> TickHarpoonRopeControllerLifetime (auto-break on expire)

Operator leaves:
  -> SlaveManager.UnbindSlave -> OnOperatorLeftSlave -> BreakRopeForClients

Player unhang (climb off):
  -> CSUnhangPacket -> BreakRopeForClients
```

### 6. InitializeTerrain Was a Stub

**Root Cause**: `InitializeTerrain()` was an empty method. The heightmap collision data was never loaded into the physics world.

| Problem | Effect | Fix |
|---------|--------|-----|
| Empty `InitializeTerrain()` stub | Physics engine has no terrain geometry; Jitter2 broadphase has no heightmap | Full implementation: loads all world cells, builds `Heightmap` → `HeightmapTester` → registers with `_physWorld.BroadPhaseFilter` and `DynamicTree` |
| No `GeoDataMode` guard for barriers | Static barrier resolution ran unconditionally (or never) | Added `GeoDataMode` check + `ShipStaticBarrierBaiIngestor.EnsureCell` per ship per tick |

### 7. TargetPhysicsTps Was Hardcoded

| Problem | Effect | Fix |
|---------|--------|-----|
| TPS hardcoded to `100f` | 4x faster than reference; damping compounds too fast | Now reads from `AppConfiguration.Instance.World.TargetPhysicsTps` (default 25f) |

## Files Modified

| File | Changes |
|------|---------|
| `AAEmu.Game/Core/Managers/World/PhysicsManager.cs` | Complete rewrite: single-step physics, adaptive sleep, correct operation order, water flow as position offset, harpoon tick integration, thread-safe RemoveShip, full InitializeTerrain, configurable TPS, GeoDataMode barrier guard |
| `AAEmu.Game/Core/Packets/C2G/CSStartSkillPacket.cs` | Added harpoon launch/cut calls after successful mounted skill on Slave |
| `AAEmu.Game/Core/Packets/C2G/CSSkillControllerStatePacket.cs` | Forward rope state to `TryApplySkillControllerState` instead of just logging |
| `AAEmu.Game/Core/Packets/C2G/CSUnhangPacket.cs` | Added `BreakRopeForClients` on sticky slave after unhang |
| `AAEmu.Game/Core/Managers/SlaveManager.cs` | Added `OnOperatorLeftSlave` call in `UnbindSlave` |

## Constants Reference (matching reference project)

| Constant | Value | Where |
|----------|-------|-------|
| TargetPhysicsTps | 25 | PhysicsManager |
| SteeringResponsivenessMul | 1.45 | ShipController/ShipMotionDefaults |
| CounterSteerResponsivenessMul | 1.35 | ShipController/ShipMotionDefaults |
| TurnFullFactorAtSpeed | 2.5 m/s | ShipController/ShipMotionDefaults |
| ShipWaterDensityMul | 3.0 | Buoyancy |
| BowProbeMul / SternProbeMul | 1.5 (legacy, unused) | ShipShoreInteraction |
| ShipLaunchHarpoonSkillId | 13749 | HarpoonMechanicsDebug |
| ShipCutHarpoonRopeSkillId | 13750 | HarpoonMechanicsDebug |
| TowAccelPerMeterStretch | 1.0 m/s^2 | ShipHarpoonTowPhysics |
| TearStretchMeters | 3.0 m | ShipHarpoonRopeController |

## Testing Checklist

- [ ] Spawn a clipper/galleon -> should NOT dive under water on summon
- [ ] Steer the ship -> should respond within ~100ms to turn/throttle
- [ ] Drive toward shore -> collision should start when bow touches land (not center)
- [ ] Fire harpoon at terrain -> rope visual should appear
- [ ] Fire harpoon at another ship -> tow should work, rope visible
- [ ] Cut rope -> rope should disappear
- [ ] Leave harpoon seat -> rope should break
- [ ] Drive through river current -> ship should drift gently (not stuck/thrown)
- [ ] Ship-to-ship collision -> should push apart without explosions
- [ ] Multiple ships near cliffs -> no AccessViolationException in logs
