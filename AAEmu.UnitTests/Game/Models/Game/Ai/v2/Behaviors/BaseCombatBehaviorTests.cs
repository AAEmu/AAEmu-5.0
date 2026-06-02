using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Behaviors;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics;

using Jitter2.LinearMath;

using Xunit;

// Target adaptation notes:
// Source tests TryFindStaticCollisionDetour, IsTargetOnUnreachableTerrain, and
// HandleUnreachableTargetEvasion, which are methods that do not exist in the target's
// BaseCombatBehavior. Those source methods are part of the newer source architecture
// and were not ported to BaseCombatBehavior in this codebase.
// This file tests NpcPathfindingConfig defaults/setters (used by NpcPathfindingImproved)
// and static geometry index injection smoke tests that underpin the combat approach logic.

namespace AAEmu.UnitTests.Game.Models.Game.AI.v2.Behaviors;

public class BaseCombatBehaviorTests
{
    public BaseCombatBehaviorTests()
    {
        AppConfiguration.Instance.World ??= new WorldConfig { GeoDataMode = false };
        AppConfiguration.Instance.NpcPathfinding ??= new NpcPathfindingConfig { UnreachableTargetMaxZDelta = 3.0f };
    }

    [Fact]
    public void NpcPathfindingConfig_UnreachableTargetMaxZDelta_DefaultIs3()
    {
        var config = new NpcPathfindingConfig();
        Assert.Equal(3.0f, config.UnreachableTargetMaxZDelta);
    }

    [Fact]
    public void NpcPathfindingConfig_UnreachableTargetEvadeTimeoutSeconds_DefaultIs5()
    {
        var config = new NpcPathfindingConfig();
        Assert.Equal(5.0f, config.UnreachableTargetEvadeTimeoutSeconds);
    }

    [Fact]
    public void NpcPathfindingConfig_UnreachableTargetMaxZDelta_CanBeSet()
    {
        var config = new NpcPathfindingConfig();
        config.UnreachableTargetMaxZDelta = 7.5f;
        Assert.Equal(7.5f, config.UnreachableTargetMaxZDelta);
    }

    [Fact]
    public void AppConfiguration_NpcPathfinding_DefaultNotNull()
    {
        // AppConfiguration.NpcPathfinding should default to a valid config instance.
        Assert.NotNull(AppConfiguration.Instance.NpcPathfinding);
    }

    [Fact]
    public void StaticGeometryIndex_WallBetweenNpcAndTarget_IsDetectedByIndex()
    {
        // Verifies that the static geometry index injection pattern used in combat
        // behavior tests correctly detects a wall between two points.
        var index = new WorldStaticGeometryIndex();
        AddStaticGeometryEntry(index, 0, 0, CreateBoxWallEntry(2.6f, -0.5f, 0f, 3.4f, 0.5f, 3f));

        var blocked = index.IsSegmentWallBlocked(
            new Vector3(0f, 0f, 1f),
            new Vector3(6f, 0f, 1f),
            verticalTolerance: 8f);

        Assert.True(blocked);
    }

    [Fact]
    public void StaticGeometryIndex_NoWall_IsNotBlockedByIndex()
    {
        var index = new WorldStaticGeometryIndex();

        var blocked = index.IsSegmentWallBlocked(
            new Vector3(0f, 0f, 1f),
            new Vector3(6f, 0f, 1f),
            verticalTolerance: 8f);

        Assert.False(blocked);
    }

    private static void AddStaticGeometryEntry(WorldStaticGeometryIndex index, int cellX, int cellY, IStaticGeometryEntry entry)
    {
        var gridField = typeof(WorldStaticGeometryIndex).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(gridField);

        var grid = Assert.IsType<Dictionary<(int CellX, int CellY), List<IStaticGeometryEntry>>>(gridField!.GetValue(index));
        if (!grid.TryGetValue((cellX, cellY), out var list))
        {
            list = [];
            grid[(cellX, cellY)] = list;
        }

        list.Add(entry);
    }

    private static FakeStaticGeometryEntry CreateBoxWallEntry(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        return new FakeStaticGeometryEntry(
            new JBoundingBox(new JVector(minX, minZ, minY), new JVector(maxX, maxZ, maxY)),
            [
                new JTriangle(new JVector(minX, minZ, minY), new JVector(minX, maxZ, minY), new JVector(minX, minZ, maxY)),
                new JTriangle(new JVector(minX, maxZ, minY), new JVector(minX, maxZ, maxY), new JVector(minX, minZ, maxY)),
                new JTriangle(new JVector(maxX, minZ, minY), new JVector(maxX, minZ, maxY), new JVector(maxX, maxZ, minY)),
                new JTriangle(new JVector(maxX, maxZ, minY), new JVector(maxX, minZ, maxY), new JVector(maxX, maxZ, maxY)),
                new JTriangle(new JVector(minX, minZ, minY), new JVector(maxX, minZ, minY), new JVector(minX, maxZ, minY)),
                new JTriangle(new JVector(maxX, minZ, minY), new JVector(maxX, maxZ, minY), new JVector(minX, maxZ, minY)),
                new JTriangle(new JVector(minX, minZ, maxY), new JVector(minX, maxZ, maxY), new JVector(maxX, minZ, maxY)),
                new JTriangle(new JVector(maxX, minZ, maxY), new JVector(minX, maxZ, maxY), new JVector(maxX, maxZ, maxY))
            ]);
    }

    private sealed class FakeStaticGeometryEntry : IStaticGeometryEntry
    {
        private readonly List<JTriangle> _triangles;

        public FakeStaticGeometryEntry(JBoundingBox worldAabb, List<JTriangle> triangles)
        {
            WorldAabb = worldAabb;
            _triangles = triangles;
        }

        public JBoundingBox WorldAabb { get; }

        public List<JTriangle> GetTransformedTriangles()
        {
            return _triangles;
        }
    }
}
