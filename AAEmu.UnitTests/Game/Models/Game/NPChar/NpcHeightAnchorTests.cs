using System;
using System.Numerics;

using AAEmu.Game.Models.Game.NPChar;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

/// <summary>
/// Тесты для проверки логики якоря высоты НПС.
/// These tests verify the height anchor logic that prevents NPCs from falling through elevated floors.
/// </summary>
public class NpcHeightAnchorTests
{
    #region NpcHeightState tests

    [Fact]
    public void NpcHeightState_DefaultAnchorKind_IsUnknown()
    {
        var state = new NpcHeightState();

        Assert.Equal(NpcHeightAnchorKind.Unknown, state.AnchorKind);
        Assert.False(state.IsInitialized);
    }

    [Fact]
    public void NpcHeightState_Capture_SetsAnchorKindAndZ()
    {
        var state = new NpcHeightState();
        state.Capture(100f, NpcHeightAnchorKind.Terrain, false);

        Assert.True(state.IsInitialized);
        Assert.Equal(NpcHeightAnchorKind.Terrain, state.AnchorKind);
        Assert.Equal(100f, state.LastResolvedZ);
        Assert.False(state.LayerLocked);
    }

    [Fact]
    public void NpcHeightState_Capture_InteriorFloor_LocksLayer()
    {
        var state = new NpcHeightState();
        state.Capture(50f, NpcHeightAnchorKind.InteriorFloor, true);

        Assert.True(state.LayerLocked);
        Assert.Equal(NpcHeightAnchorKind.InteriorFloor, state.AnchorKind);
    }

    [Fact]
    public void NpcHeightState_Capture_Air_LocksLayer()
    {
        var state = new NpcHeightState();
        state.Capture(200f, NpcHeightAnchorKind.Air, true);

        Assert.True(state.LayerLocked);
        Assert.Equal(NpcHeightAnchorKind.Air, state.AnchorKind);
    }

    [Fact]
    public void NpcHeightState_Capture_WaterSurface_LocksLayer()
    {
        var state = new NpcHeightState();
        state.Capture(10f, NpcHeightAnchorKind.WaterSurface, true);

        Assert.True(state.LayerLocked);
        Assert.Equal(NpcHeightAnchorKind.WaterSurface, state.AnchorKind);
    }

    [Fact]
    public void NpcHeightState_Capture_WaterColumn_LocksLayer()
    {
        var state = new NpcHeightState();
        state.Capture(15f, NpcHeightAnchorKind.WaterColumn, true);

        Assert.True(state.LayerLocked);
        Assert.Equal(NpcHeightAnchorKind.WaterColumn, state.AnchorKind);
    }

    [Fact]
    public void NpcHeightState_Capture_Spawner_LocksLayer()
    {
        var state = new NpcHeightState();
        state.Capture(75f, NpcHeightAnchorKind.Spawner, true);

        Assert.True(state.LayerLocked);
        Assert.Equal(NpcHeightAnchorKind.Spawner, state.AnchorKind);
    }

    #endregion

    #region NpcHeightAnchorKind enum tests

    [Fact]
    public void NpcHeightAnchorKind_HasAllExpectedValues()
    {
        // Verify all expected anchor kinds exist (order: Unknown, Terrain, Spawner, InteriorFloor, WaterSurface, WaterColumn, Air)
        Assert.Equal(NpcHeightAnchorKind.Unknown, (NpcHeightAnchorKind)0);
        Assert.Equal(NpcHeightAnchorKind.Terrain, (NpcHeightAnchorKind)1);
        Assert.Equal(NpcHeightAnchorKind.Spawner, (NpcHeightAnchorKind)2);
        Assert.Equal(NpcHeightAnchorKind.InteriorFloor, (NpcHeightAnchorKind)3);
        Assert.Equal(NpcHeightAnchorKind.WaterSurface, (NpcHeightAnchorKind)4);
        Assert.Equal(NpcHeightAnchorKind.WaterColumn, (NpcHeightAnchorKind)5);
        Assert.Equal(NpcHeightAnchorKind.Air, (NpcHeightAnchorKind)6);
    }

    #endregion

    #region IsInitialized property tests

    [Fact]
    public void NpcHeightState_IsInitialized_FalseByDefault()
    {
        var state = new NpcHeightState();
        Assert.False(state.IsInitialized);
    }

    [Fact]
    public void NpcHeightState_IsInitialized_TrueAfterCapture()
    {
        var state = new NpcHeightState();
        state.Capture(10f, NpcHeightAnchorKind.Terrain, false);
        Assert.True(state.IsInitialized);
    }

    #endregion
}