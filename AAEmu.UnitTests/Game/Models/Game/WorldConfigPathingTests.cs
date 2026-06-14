using AAEmu.Game.Models.Game;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game;

/// <summary>
/// Unit tests for the new <see cref="WorldConfig.SpawnHeight"/> and
/// <see cref="WorldConfig.Pathing"/> properties.
/// </summary>
public class WorldConfigPathingTests
{
    [Fact]
    public void WorldConfig_SpawnHeight_DefaultIsNotNull()
    {
        var config = new WorldConfig();
        Assert.NotNull(config.SpawnHeight);
    }

    [Fact]
    public void WorldConfig_SpawnHeight_DefaultValues()
    {
        var config = new WorldConfig();
        var sh = config.SpawnHeight;

        Assert.True(sh.Enabled);
        Assert.Equal(6.0f, sh.MaxBaiDistance2D);
        Assert.Equal(3.0f, sh.MaxBaiVsHeightmapDelta);
        Assert.Equal(5.0f, sh.MaxSnapDistance);
        Assert.Equal(1.5f, sh.UnmeshedStructureThreshold);
        Assert.Equal(0.5f, sh.MaxDownwardSnapDistance);
        Assert.Equal(3.0f, sh.SpawnHomeXyRadius);
        Assert.Equal(4.0f, sh.SpawnHomeBlendBand);
        Assert.True(sh.PreferHeightmapForGroundZ);
        Assert.False(sh.LogResolution);
        Assert.False(sh.UseLegacyBai3DLookup);
        Assert.False(sh.TrustBaiNearSpawnZ);
    }

    [Fact]
    public void WorldConfig_Pathing_DefaultIsNotNull()
    {
        var config = new WorldConfig();
        Assert.NotNull(config.Pathing);
    }

    [Fact]
    public void WorldConfig_Pathing_DefaultValues()
    {
        var config = new WorldConfig();
        var p = config.Pathing;

        Assert.True(p.Enabled);
        Assert.Equal(2.5f, p.RepathTargetMovedDistance);
        Assert.Equal(350, p.RepathCooldownMs);
        Assert.Equal(4.0f, p.SkipStartNodeIfBehindRadius);
        Assert.Equal(0.9f, p.WaypointReachTolerance);
        Assert.True(p.DirectFallbackWhenVisible);
        Assert.Equal(4, p.ReducePathMaxSkip);
        Assert.True(p.UseCumulativeAStarCost);
        Assert.Equal(8.0f, p.MaxStartEndNode2D);
        Assert.False(p.LogPathfinding);
        Assert.True(p.UseOptimizedAStar);
        Assert.True(p.UseEdgeLengthCost);
        Assert.Equal(15, p.LoopBudgetDistanceMultiplier);
        Assert.Equal(100, p.LoopBudgetBase);
        Assert.False(p.RejectDetourPaths);
        Assert.Equal(5.0f, p.DetourRejectThreshold);
    }

    [Fact]
    public void WorldConfig_SpawnHeight_CanBeReplaced()
    {
        var config = new WorldConfig();
        var newConfig = new SpawnHeightConfig { Enabled = false, MaxSnapDistance = 99f };
        config.SpawnHeight = newConfig;

        Assert.False(config.SpawnHeight.Enabled);
        Assert.Equal(99f, config.SpawnHeight.MaxSnapDistance);
    }

    [Fact]
    public void WorldConfig_Pathing_CanBeReplaced()
    {
        var config = new WorldConfig();
        var newConfig = new PathingConfig { Enabled = false, RepathCooldownMs = 1000 };
        config.Pathing = newConfig;

        Assert.False(config.Pathing.Enabled);
        Assert.Equal(1000, config.Pathing.RepathCooldownMs);
    }
}
