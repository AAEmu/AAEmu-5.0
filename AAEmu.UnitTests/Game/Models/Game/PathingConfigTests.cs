using AAEmu.Game.Models.Game;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game;

/// <summary>
/// Unit tests for <see cref="PathingConfig"/> default values and property setters.
/// </summary>
public class PathingConfigTests
{
    [Fact]
    public void Defaults_AreConservative()
    {
        var config = new PathingConfig();

        // Master switch
        Assert.True(config.Enabled);

        // Repath settings
        Assert.Equal(2.5f, config.RepathTargetMovedDistance);
        Assert.Equal(350, config.RepathCooldownMs);

        // Start node skip
        Assert.Equal(4.0f, config.SkipStartNodeIfBehindRadius);

        // Waypoint tolerance
        Assert.Equal(0.9f, config.WaypointReachTolerance);

        // Direct fallback
        Assert.True(config.DirectFallbackWhenVisible);

        // Path smoothing
        Assert.Equal(4, config.ReducePathMaxSkip);

        // A* cost
        Assert.True(config.UseCumulativeAStarCost);

        // Start/end node 2D distance
        Assert.Equal(8.0f, config.MaxStartEndNode2D);

        // Logging
        Assert.False(config.LogPathfinding);

        // v6 optimized A*
        Assert.True(config.UseOptimizedAStar);
        Assert.True(config.UseEdgeLengthCost);

        // v6 loop budget
        Assert.Equal(15, config.LoopBudgetDistanceMultiplier);
        Assert.Equal(100, config.LoopBudgetBase);

        // v6 detour rejection (disabled by default)
        Assert.False(config.RejectDetourPaths);
        Assert.Equal(5.0f, config.DetourRejectThreshold);
    }

    [Fact]
    public void Enabled_CanBeDisabled()
    {
        var config = new PathingConfig { Enabled = false };
        Assert.False(config.Enabled);
    }

    [Fact]
    public void RepathTargetMovedDistance_CanBeSet()
    {
        var config = new PathingConfig { RepathTargetMovedDistance = 5.0f };
        Assert.Equal(5.0f, config.RepathTargetMovedDistance);
    }

    [Fact]
    public void RepathCooldownMs_CanBeSet()
    {
        var config = new PathingConfig { RepathCooldownMs = 500 };
        Assert.Equal(500, config.RepathCooldownMs);
    }

    [Fact]
    public void SkipStartNodeIfBehindRadius_CanBeSet()
    {
        var config = new PathingConfig { SkipStartNodeIfBehindRadius = 0f };
        Assert.Equal(0f, config.SkipStartNodeIfBehindRadius);
    }

    [Fact]
    public void WaypointReachTolerance_CanBeSet()
    {
        var config = new PathingConfig { WaypointReachTolerance = 1.5f };
        Assert.Equal(1.5f, config.WaypointReachTolerance);
    }

    [Fact]
    public void DirectFallbackWhenVisible_CanBeDisabled()
    {
        var config = new PathingConfig { DirectFallbackWhenVisible = false };
        Assert.False(config.DirectFallbackWhenVisible);
    }

    [Fact]
    public void ReducePathMaxSkip_CanBeSet()
    {
        var config = new PathingConfig { ReducePathMaxSkip = 0 };
        Assert.Equal(0, config.ReducePathMaxSkip);
    }

    [Fact]
    public void UseCumulativeAStarCost_CanBeDisabled()
    {
        var config = new PathingConfig { UseCumulativeAStarCost = false };
        Assert.False(config.UseCumulativeAStarCost);
    }

    [Fact]
    public void MaxStartEndNode2D_CanBeSet()
    {
        var config = new PathingConfig { MaxStartEndNode2D = 12.0f };
        Assert.Equal(12.0f, config.MaxStartEndNode2D);
    }

    [Fact]
    public void LogPathfinding_CanBeEnabled()
    {
        var config = new PathingConfig { LogPathfinding = true };
        Assert.True(config.LogPathfinding);
    }

    [Fact]
    public void UseOptimizedAStar_CanBeDisabled()
    {
        var config = new PathingConfig { UseOptimizedAStar = false };
        Assert.False(config.UseOptimizedAStar);
    }

    [Fact]
    public void UseEdgeLengthCost_CanBeDisabled()
    {
        var config = new PathingConfig { UseEdgeLengthCost = false };
        Assert.False(config.UseEdgeLengthCost);
    }

    [Fact]
    public void LoopBudgetDistanceMultiplier_CanBeSet()
    {
        var config = new PathingConfig { LoopBudgetDistanceMultiplier = 20 };
        Assert.Equal(20, config.LoopBudgetDistanceMultiplier);
    }

    [Fact]
    public void LoopBudgetBase_CanBeSet()
    {
        var config = new PathingConfig { LoopBudgetBase = 200 };
        Assert.Equal(200, config.LoopBudgetBase);
    }

    [Fact]
    public void RejectDetourPaths_CanBeEnabled()
    {
        var config = new PathingConfig { RejectDetourPaths = true };
        Assert.True(config.RejectDetourPaths);
    }

    [Fact]
    public void DetourRejectThreshold_CanBeSet()
    {
        var config = new PathingConfig { DetourRejectThreshold = 10.0f };
        Assert.Equal(10.0f, config.DetourRejectThreshold);
    }
}
