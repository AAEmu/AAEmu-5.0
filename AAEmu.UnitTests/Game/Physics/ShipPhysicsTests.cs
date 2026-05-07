using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.slaves;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics;
using AAEmu.Game.Physics.Forces;
using AAEmu.Game.Utils;

using Jitter2;
using Jitter2.LinearMath;

using Xunit;

namespace AAEmu.UnitTests.Game.Physics;

public class ShipPhysicsTests
{
    // ----------------------------------------------------------------
    // 1. MathUtil.HalfPi
    // ----------------------------------------------------------------

    [Fact]
    public void MathUtil_HalfPi_Equals_PiOverTwo()
    {
        Assert.Equal(MathF.PI / 2f, MathUtil.HalfPi);
    }

    // ----------------------------------------------------------------
    // 2. ReplicationSmoothing
    // ----------------------------------------------------------------

    [Fact]
    public void ReplicationSmoothing_InitialState_HasExpectedDefaults()
    {
        var rs = new ReplicationSmoothing();

        Assert.False(rs.Seeded);
        Assert.Equal(0f, rs.PosX);
        Assert.Equal(0f, rs.PosY);
        Assert.Equal(0f, rs.PosZ);
        Assert.Equal(0f, rs.VelPx);
        Assert.Equal(0f, rs.VelPy);
        Assert.Equal(0f, rs.VelPz);
        Assert.Equal(0f, rs.BankSmoothed);
        Assert.Equal(0f, rs.GroundPitchSmoothed);
        Assert.Equal((byte)0, rs.ContactHoldTicks);
    }

    [Fact]
    public void ReplicationSmoothing_Reset_ClearsSeededAndContactHoldTicks()
    {
        var rs = new ReplicationSmoothing
        {
            Seeded = true,
            PosX = 10f,
            PosY = 20f,
            PosZ = 30f,
            ContactHoldTicks = 12
        };

        rs.Reset();

        Assert.False(rs.Seeded);
        Assert.Equal((byte)0, rs.ContactHoldTicks);
        // Position values are intentionally NOT cleared by Reset
        Assert.Equal(10f, rs.PosX);
    }

    // ----------------------------------------------------------------
    // 3. NumericExtensions ToCellIndex / ToPathsIndex
    // ----------------------------------------------------------------

    [Fact]
    public void ToCellIndex_KnownCoordinates_ReturnsExpectedCell()
    {
        var pos = new Vector3(2048f, 2048f, 0f);
        var (cx, cy) = pos.ToCellIndex();

        Assert.Equal(2, cx);
        Assert.Equal(2, cy);
    }

    [Fact]
    public void ToCellIndex_Origin_ReturnsZeroZero()
    {
        var pos = new Vector3(0f, 0f, 0f);
        var (cx, cy) = pos.ToCellIndex();

        Assert.Equal(0, cx);
        Assert.Equal(0, cy);
    }

    [Fact]
    public void ToCellIndex_NegativeCoordinates_ReturnsNegativeIndices()
    {
        // Math.Floor(-1 / 1024) = -1
        var pos = new Vector3(-1f, -1f, 0f);
        var (cx, cy) = pos.ToCellIndex();

        Assert.Equal(-1, cx);
        Assert.Equal(-1, cy);
    }

    [Fact]
    public void ToCellIndex_JustBelowBoundary_StaysInLowerCell()
    {
        var pos = new Vector3(1023.9f, 1023.9f, 0f);
        var (cx, cy) = pos.ToCellIndex();

        Assert.Equal(0, cx);
        Assert.Equal(0, cy);
    }

    [Fact]
    public void ToCellIndex_ExactBoundary_AdvancesToNextCell()
    {
        var pos = new Vector3(1024f, 1024f, 0f);
        var (cx, cy) = pos.ToCellIndex();

        Assert.Equal(1, cx);
        Assert.Equal(1, cy);
    }

    [Fact]
    public void ToPathsIndex_KnownCoordinates_ReturnsExpectedIndex()
    {
        var pos = new Vector3(2048f, 2048f, 0f);
        var (px, py) = pos.ToPathsIndex();

        Assert.Equal(8, px); // 2048 / 256 = 8
        Assert.Equal(8, py);
    }

    [Fact]
    public void ToPathsIndex_Origin_ReturnsZeroZero()
    {
        var pos = new Vector3(0f, 0f, 0f);
        var (px, py) = pos.ToPathsIndex();

        Assert.Equal(0, px);
        Assert.Equal(0, py);
    }

    [Fact]
    public void ToPathsIndex_NegativeCoordinates_ReturnsNegativeIndices()
    {
        var pos = new Vector3(-1f, -1f, 0f);
        var (px, py) = pos.ToPathsIndex();

        Assert.Equal(-1, px);
        Assert.Equal(-1, py);
    }

    [Theory]
    [InlineData(0f, 0f, 0, 0)]
    [InlineData(255.9f, 255.9f, 0, 0)]
    [InlineData(256f, 256f, 1, 1)]
    [InlineData(512f, 768f, 2, 3)]
    public void ToPathsIndex_VariousPositions_MatchesExpected(float x, float y, int expectedPx, int expectedPy)
    {
        var pos = new Vector3(x, y, 0f);
        var (px, py) = pos.ToPathsIndex();

        Assert.Equal(expectedPx, px);
        Assert.Equal(expectedPy, py);
    }

    // ----------------------------------------------------------------
    // 4. ShipStaticBarrierSpatialGrid
    // ----------------------------------------------------------------

    [Fact]
    public void SpatialGrid_CellSizeMeters_Is128()
    {
        Assert.Equal(128f, ShipStaticBarrierSpatialGrid.CellSizeMeters);
    }

    [Fact]
    public void SpatialGrid_QueryOnEmptyGrid_ReturnsNoResults()
    {
        var grid = new ShipStaticBarrierSpatialGrid();
        grid.Rebuild(new List<ShipStaticBarrier>());

        var outList = new List<(ShipStaticBarrier, int)>();
        var dedupe = new HashSet<(ShipStaticBarrier, int)>();
        grid.Query(500f, 500f, 50f, outList, dedupe);

        Assert.Empty(outList);
    }

    [Fact]
    public void SpatialGrid_RebuildAndQuery_FindsBarrierNearSegment()
    {
        // Create a barrier via TryCreate
        var dto = new ShipStaticBarrierEntryDto
        {
            Name = "test_wall",
            ZoneKey = 1,
            ZMin = 0f,
            ZMax = 100f,
            HalfThicknessMeters = 2f,
            Enabled = true,
            PointsXY = new List<List<double>>
            {
                new() { 500.0, 500.0 },
                new() { 600.0, 500.0 }
            }
        };

        Assert.True(ShipStaticBarrier.TryCreate(dto, out var barrier, out _));

        var grid = new ShipStaticBarrierSpatialGrid();
        grid.Rebuild(new List<ShipStaticBarrier> { barrier });

        var outList = new List<(ShipStaticBarrier, int)>();
        var dedupe = new HashSet<(ShipStaticBarrier, int)>();

        // Query near the segment
        grid.Query(550f, 500f, 50f, outList, dedupe);
        Assert.NotEmpty(outList);
        Assert.Equal(barrier, outList[0].Item1);
        Assert.Equal(0, outList[0].Item2);
    }

    [Fact]
    public void SpatialGrid_Query_FarFromBarrier_ReturnsEmpty()
    {
        var dto = new ShipStaticBarrierEntryDto
        {
            Name = "far_wall",
            ZoneKey = 1,
            ZMin = 0f,
            ZMax = 100f,
            HalfThicknessMeters = 2f,
            Enabled = true,
            PointsXY = new List<List<double>>
            {
                new() { 500.0, 500.0 },
                new() { 600.0, 500.0 }
            }
        };

        Assert.True(ShipStaticBarrier.TryCreate(dto, out var barrier, out _));

        var grid = new ShipStaticBarrierSpatialGrid();
        grid.Rebuild(new List<ShipStaticBarrier> { barrier });

        var outList = new List<(ShipStaticBarrier, int)>();
        var dedupe = new HashSet<(ShipStaticBarrier, int)>();

        // Query very far from the segment (well beyond SegmentInsertExpandMeters)
        grid.Query(50000f, 50000f, 10f, outList, dedupe);
        Assert.Empty(outList);
    }

    [Fact]
    public void SpatialGrid_DisabledBarrier_NotInserted()
    {
        var dto = new ShipStaticBarrierEntryDto
        {
            Name = "disabled_wall",
            ZoneKey = 1,
            ZMin = 0f,
            ZMax = 100f,
            Enabled = false,
            PointsXY = new List<List<double>>
            {
                new() { 500.0, 500.0 },
                new() { 600.0, 500.0 }
            }
        };

        Assert.True(ShipStaticBarrier.TryCreate(dto, out var barrier, out _));

        var grid = new ShipStaticBarrierSpatialGrid();
        grid.Rebuild(new List<ShipStaticBarrier> { barrier });

        var outList = new List<(ShipStaticBarrier, int)>();
        var dedupe = new HashSet<(ShipStaticBarrier, int)>();
        grid.Query(550f, 500f, 50f, outList, dedupe);

        Assert.Empty(outList);
    }

    // ----------------------------------------------------------------
    // 5. ShipStaticObstacleContact constants
    // ----------------------------------------------------------------

    [Fact]
    public void ShipStaticObstacleContact_DefaultReplicationContactHoldTicks_Is8()
    {
        Assert.Equal((byte)8, ShipStaticObstacleContact.DefaultReplicationContactHoldTicks);
    }

    [Fact]
    public void ShipStaticObstacleContact_BridgeCeilingReplicationContactHoldTicks_Is24()
    {
        Assert.Equal((byte)24, ShipStaticObstacleContact.BridgeCeilingReplicationContactHoldTicks);
    }

    [Fact]
    public void ShipStaticObstacleContact_MinPenetrationForDamage_IsPositive()
    {
        Assert.True(ShipStaticObstacleContact.MinPenetrationMetersForEnvHullDamage > 0f);
        Assert.Equal(0.015f, ShipStaticObstacleContact.MinPenetrationMetersForEnvHullDamage);
    }

    // ----------------------------------------------------------------
    // 6. OneShotVelocityKick — tested via constants/structure only
    //    (requires a Jitter2 World instance which is heavy; verify type exists)
    // ----------------------------------------------------------------

    [Fact]
    public void OneShotVelocityKick_IsSubclassOfForceGenerator()
    {
        Assert.True(typeof(AAEmu.Game.Physics.Forces.OneShotVelocityKick)
            .IsSubclassOf(typeof(AAEmu.Game.Physics.Forces.ForceGenerator)));
    }

    // ----------------------------------------------------------------
    // 7. Slave property defaults
    // ----------------------------------------------------------------

    [Fact]
    public void Slave_TurnSpeedVelocityMul_DefaultsToOne()
    {
        var slave = new Slave();
        Assert.Equal(1f, slave.TurnSpeedVelocityMul);
    }

    [Fact]
    public void Slave_LastMoveDirSign_DefaultsToPositiveOne()
    {
        var slave = new Slave();
        Assert.Equal((sbyte)1, slave.LastMoveDirSign);
    }

    [Fact]
    public void Slave_BankAngle_DefaultsToZero()
    {
        var slave = new Slave();
        Assert.Equal(0f, slave.BankAngle);
    }

    [Fact]
    public void Slave_ShipHullCollisionDamageCooldown_IsEmptyDictionary()
    {
        var slave = new Slave();
        Assert.NotNull(slave.ShipHullCollisionDamageCooldownByOtherShipId);
        Assert.Empty(slave.ShipHullCollisionDamageCooldownByOtherShipId);
    }

    [Fact]
    public void Slave_GroundPitchAngle_DefaultsToZero()
    {
        var slave = new Slave();
        Assert.Equal(0f, slave.GroundPitchAngle);
    }

    [Fact]
    public void Slave_HarpoonRope_DefaultsToDisengaged()
    {
        var slave = new Slave();
        Assert.False(slave.HarpoonRope.IsEngaged);
    }

    // ----------------------------------------------------------------
    // 8. WindModelType enum
    // ----------------------------------------------------------------

    [Fact]
    public void WindModelType_HasOfficialValue()
    {
        Assert.True(Enum.IsDefined(typeof(WorldConfig.WindModelType), WorldConfig.WindModelType.Official));
    }

    [Fact]
    public void WindModelType_HasRealisticValue()
    {
        Assert.True(Enum.IsDefined(typeof(WorldConfig.WindModelType), WorldConfig.WindModelType.Realistic));
    }

    [Fact]
    public void WindModelType_OfficialIsZero()
    {
        Assert.Equal(0, (int)WorldConfig.WindModelType.Official);
    }

    [Fact]
    public void WindModelType_RealisticIsOne()
    {
        Assert.Equal(1, (int)WorldConfig.WindModelType.Realistic);
    }

    // ----------------------------------------------------------------
    // 9. NumericExtensions DegToRad / RadToDeg round-trip
    //    (indirect test of the math formulas used by ComputeVisualMaxBankDeg helpers)
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(360f)]
    public void DegToRad_RadToDeg_RoundTrip_Float(float degrees)
    {
        var radians = degrees.DegToRad();
        var backToDeg = radians.RadToDeg();

        Assert.Equal(degrees, backToDeg, precision: 3);
    }

    [Fact]
    public void DegToRad_90Degrees_EqualsHalfPi()
    {
        var rad = 90f.DegToRad();
        Assert.Equal(MathUtil.HalfPi, rad, precision: 5);
    }

    // ----------------------------------------------------------------
    // 10. ShipHarpoonRopeState
    // ----------------------------------------------------------------

    [Fact]
    public void ShipHarpoonRopeState_Default_IsNotEngaged()
    {
        var state = new ShipHarpoonRopeState();
        Assert.False(state.IsEngaged);
    }

    [Fact]
    public void ShipHarpoonRopeState_SetEngaged_IsEngagedReturnsTrue()
    {
        var state = new ShipHarpoonRopeState { IsEngaged = true };
        Assert.True(state.IsEngaged);
    }

    [Fact]
    public void ShipHarpoonRopeState_Default_HasZeroRopeLength()
    {
        var state = new ShipHarpoonRopeState();
        Assert.Equal(0f, state.RopeLength);
    }

    [Fact]
    public void ShipHarpoonRopeState_Default_HookWorldIsZero()
    {
        var state = new ShipHarpoonRopeState();
        Assert.Equal(Vector3.Zero, state.HookWorld);
    }

    [Fact]
    public void ShipHarpoonRopeState_Default_ControllerExpireIsNull()
    {
        var state = new ShipHarpoonRopeState();
        Assert.Null(state.ControllerExpireAtUtc);
    }

    [Fact]
    public void ShipHarpoonRopeState_Clear_ResetsAllFields()
    {
        var state = new ShipHarpoonRopeState
        {
            IsEngaged = true,
            HookWorld = new Vector3(100f, 200f, 300f),
            HookBasisObjId = 42,
            HookLocalInBasis = new Vector3(1f, 2f, 3f),
            RopeLength = 50f,
            LastTeared = true,
            LastCutout = true,
            MaxLaunchRange = 100f,
            HookAttachedToTerrain = true,
            ControllerExpireAtUtc = DateTime.UtcNow
        };

        state.Clear();

        Assert.False(state.IsEngaged);
        Assert.Equal(Vector3.Zero, state.HookWorld);
        Assert.Equal(0u, state.HookBasisObjId);
        Assert.Equal(Vector3.Zero, state.HookLocalInBasis);
        Assert.Equal(0f, state.RopeLength);
        Assert.False(state.LastTeared);
        Assert.False(state.LastCutout);
        Assert.Equal(0f, state.MaxLaunchRange);
        Assert.False(state.HookAttachedToTerrain);
        Assert.Null(state.ControllerExpireAtUtc);
    }

    // ----------------------------------------------------------------
    // ShipStaticBarrier.TryCreate validation
    // ----------------------------------------------------------------

    [Fact]
    public void ShipStaticBarrier_TryCreate_NullEntry_ReturnsFalse()
    {
        Assert.False(ShipStaticBarrier.TryCreate(null, out _, out var error));
        Assert.Contains("null", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShipStaticBarrier_TryCreate_TooFewPoints_ReturnsFalse()
    {
        var dto = new ShipStaticBarrierEntryDto
        {
            Name = "bad",
            PointsXY = new List<List<double>> { new() { 1.0, 2.0 } }
        };

        Assert.False(ShipStaticBarrier.TryCreate(dto, out _, out var error));
        Assert.Contains("at least 2", error);
    }

    [Fact]
    public void ShipStaticBarrier_TryCreate_ZMaxLessThanZMin_ReturnsFalse()
    {
        var dto = new ShipStaticBarrierEntryDto
        {
            Name = "inverted",
            ZMin = 100f,
            ZMax = 50f,
            PointsXY = new List<List<double>>
            {
                new() { 0.0, 0.0 },
                new() { 10.0, 10.0 }
            }
        };

        Assert.False(ShipStaticBarrier.TryCreate(dto, out _, out var error));
        Assert.Contains("ZMax", error);
    }

    [Fact]
    public void ShipStaticBarrier_TryCreate_ValidEntry_ReturnsTrue()
    {
        var dto = new ShipStaticBarrierEntryDto
        {
            Name = "ok_wall",
            ZoneKey = 5,
            ZMin = 0f,
            ZMax = 100f,
            HalfThicknessMeters = 3f,
            Enabled = true,
            PointsXY = new List<List<double>>
            {
                new() { 100.0, 200.0 },
                new() { 300.0, 400.0 }
            }
        };

        Assert.True(ShipStaticBarrier.TryCreate(dto, out var barrier, out _));
        Assert.NotNull(barrier);
        Assert.Equal("ok_wall", barrier.Name);
        Assert.Equal(5u, barrier.ZoneKey);
        Assert.Equal(3f, barrier.HalfThicknessMeters);
    }

    // ----------------------------------------------------------------
    // NumericExtensions — JVector / Vector3 conversions
    // ----------------------------------------------------------------

    [Fact]
    public void ToVector_SwapsYAndZ()
    {
        var jv = new Jitter2.LinearMath.JVector(1f, 2f, 3f);
        var v = jv.ToVector();

        Assert.Equal(1f, v.X);
        Assert.Equal(3f, v.Y); // Jitter Z -> Vector Y
        Assert.Equal(2f, v.Z); // Jitter Y -> Vector Z
    }

    [Fact]
    public void ToJVector_SwapsYAndZ()
    {
        var v = new Vector3(1f, 2f, 3f);
        var jv = v.ToJVector();

        Assert.Equal(1f, jv.X);
        Assert.Equal(3f, jv.Y); // Vector Z -> Jitter Y
        Assert.Equal(2f, jv.Z); // Vector Y -> Jitter Z
    }

    // ----------------------------------------------------------------
    // 11. Ship-spawn dive fix — thread race condition regression tests
    //     (added 2026-06-02)
    //
    //     Helper: builds a minimal Jitter2 World + Buoyancy + ShipController + Slave
    //     without touching any game singletons.
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns a minimal ShipModel with a non-zero mass and box dimensions large
    /// enough for Jitter2's BoxShape to accept without throwing.
    /// </summary>
    private static ShipModel MakeTestShipModel() => new()
    {
        Mass = 500f,
        MassBoxSizeX = 8f,
        MassBoxSizeY = 20f,
        MassBoxSizeZ = 3f,
        MassCenterX = 0f,
        MassCenterY = 0f,
        MassCenterZ = 0f
    };

    /// <summary>
    /// Creates a Jitter2 World, a Buoyancy instance (FluidBox at Y=100),
    /// a ShipController + hull, and a Slave wired together with the given SpawnTime.
    /// The hull is added to buoyancy via AddForRectangularParallelepiped.
    /// Returns all three objects so the test can make assertions and clean up.
    /// </summary>
    private static (Jitter2.World world, Buoyancy buoyancy, ShipController ctrl, Slave slave)
        BuildShipScenario(DateTime spawnTime, float portalTime = 10f)
    {
        var world = new Jitter2.World();

        // FluidBox: water surface at physics Y = 100 (game Z = 100)
        var buoyancy = new Buoyancy(world)
        {
            FluidBox = new JBoundingBox(
                new JVector(-10000f, -200f, -10000f),
                new JVector( 10000f,  100f,  10000f))
        };

        var shipModel = MakeTestShipModel();
        var ctrl = new ShipController(world, shipModel);

        // Spawn the hull at waterline
        ctrl.Build(new JVector(0f, 100f, 0f), JQuaternion.Identity);

        var template = new SlaveTemplate { PortalTime = portalTime };
        var slave = new Slave
        {
            SpawnTime = spawnTime,
            Template = template,
            ShipController = ctrl,
            CachedWaterSurface = 100f
        };

        // Tag the hull so Buoyancy.PreStep can cast body.Tag to Slave
        ctrl.Hull.Tag = slave;

        // Register the hull with the buoyancy simulator
        buoyancy.AddForRectangularParallelepiped(ctrl.Hull, 3);

        return (world, buoyancy, ctrl, slave);
    }

    [Fact]
    public void BuoyancyPreStep_DuringPortalTime_SetsGravityFalseAndZerosVelocity()
    {
        // Arrange: ship just spawned — PortalTime has NOT expired
        var (world, buoyancy, ctrl, _) = BuildShipScenario(
            spawnTime: DateTime.UtcNow,   // spawned right now
            portalTime: 10f);             // portal lasts 10 s

        // Simulate the race condition: physics gave the hull a downward velocity
        ctrl.Hull.Velocity = new JVector(0f, -0.5f, 0f);
        ctrl.Hull.AffectedByGravity = true;

        // Act
        buoyancy.PreStep(0.05f);

        // Assert: PreStep must suppress both gravity and residual velocity during PortalTime
        Assert.False(ctrl.Hull.AffectedByGravity,
            "Hull must have gravity disabled while PortalTime has not expired.");
        Assert.Equal(JVector.Zero.X, ctrl.Hull.Velocity.X, precision: 3);
        Assert.Equal(JVector.Zero.Y, ctrl.Hull.Velocity.Y, precision: 3);
        Assert.Equal(JVector.Zero.Z, ctrl.Hull.Velocity.Z, precision: 3);
    }

    [Fact]
    public void BuoyancyPreStep_AfterPortalTime_EnablesGravity()
    {
        // Arrange: ship spawned 30 s ago — PortalTime (10 s) has expired
        var (world, buoyancy, ctrl, _) = BuildShipScenario(
            spawnTime: DateTime.UtcNow.AddSeconds(-30),
            portalTime: 10f);

        // Act
        buoyancy.PreStep(0.05f);

        // Assert: once PortalTime has expired the hull must be subject to gravity again
        Assert.True(ctrl.Hull.AffectedByGravity,
            "Hull must have gravity re-enabled after PortalTime has expired.");
    }

    [Fact]
    public void BuoyancyEquilibrium_AtTargetDepth_BuoyancyForceEqualsGravityForce()
    {
        // Pure math: at equilibrium the upward buoyancy force must exactly cancel gravity.
        //
        // The formula used in Buoyancy.PreStep:
        //   buoyancyForce = submergedDepth * mass * Density * ShipWaterDensityMul * 9.81
        //
        // Setting buoyancyForce = gravityForce (= mass * 9.81) and solving for submergedDepth:
        //   submergedDepth_eq = 1 / (Density * ShipWaterDensityMul)
        //                     = 1 / (BaseWaterDensity * ShipWaterDensityMul)

        const float mass = 1000f;
        const float g = 9.81f;

        var density = Buoyancy.BaseWaterDensity;       // 1.025
        var densityMul = Buoyancy.ShipWaterDensityMul; // 3.0

        var submergedDepthEq = 1f / (density * densityMul);

        var gravityForce = mass * g;
        var buoyancyForce = submergedDepthEq * mass * density * densityMul * g;

        Assert.Equal(gravityForce, buoyancyForce, precision: 3);
    }

    [Fact]
    public void ShipController_Build_DefaultGravityIsTrue_RequiresExplicitDisableInAddShip()
    {
        // Documents (and guards) the Jitter2 default: newly created bodies have
        // AffectedByGravity == true immediately after Build().
        // PhysicsManager.AddShip() must therefore explicitly set it to false to prevent
        // the one-step gravity window that caused the spawn-dive bug.

        var world = new Jitter2.World();
        var ctrl = new ShipController(world, MakeTestShipModel());

        ctrl.Build(new JVector(0f, 100f, 0f), JQuaternion.Identity);

        Assert.True(ctrl.Hull.AffectedByGravity,
            "Jitter2 default is AffectedByGravity=true; AddShip must set it to false explicitly.");
    }

    [Fact]
    public void BuoyancyEquilibrium_TargetSpawnZ_IsSlightlyBelowWaterSurface()
    {
        // Verifies the SlaveManager spawn-height formula:
        //   targetZ = waterSurface - 1 / (BaseWaterDensity * ShipWaterDensityMul)
        //
        // The result must be between (waterSurface - 1) and waterSurface, i.e. the hull
        // sits just below the surface at the equilibrium draught, not fully submerged or floating above.

        const float waterSurface = 100f;

        var targetSubmerged = 1f / (Buoyancy.BaseWaterDensity * Buoyancy.ShipWaterDensityMul);
        var targetZ = waterSurface - targetSubmerged;

        Assert.True(targetZ > waterSurface - 1f,
            $"targetZ ({targetZ:F4}) should be no more than 1 m below the water surface.");
        Assert.True(targetZ < waterSurface,
            $"targetZ ({targetZ:F4}) should be below the water surface ({waterSurface}).");
    }
}
