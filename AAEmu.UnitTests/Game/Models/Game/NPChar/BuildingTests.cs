using System;

using AAEmu.Game.Models.Game.NPChar;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

/// <summary>
/// Unit tests for the <see cref="Building"/> helper class that resolves
/// floor heights inside multi-story structures.
/// </summary>
public class BuildingTests
{
    #region GetFloorHeight(float, float, int) — by index

    [Fact]
    public void GetFloorHeight_SingleFloor_ReturnsMidpoint()
    {
        // When vertical difference is below the 5.6f threshold, only 1 floor exists.
        var result = Building.GetFloorHeight(10f, 15f, 0);
        Assert.Equal(12.5f, result);
    }

    [Fact]
    public void GetFloorHeight_SingleFloor_ThrowsOnInvalidIndex()
    {
        // Only 1 floor exists (index 0). Index 1 should throw.
        Assert.Throws<ArgumentOutOfRangeException>(() => Building.GetFloorHeight(10f, 15f, 1));
    }

    [Fact]
    public void GetFloorHeight_TwoFloors_FirstFloor()
    {
        // verticalDiff = 5.6f → 2 floors
        var result = Building.GetFloorHeight(10f, 15.6f, 0);
        Assert.Equal(10f, result);
    }

    [Fact]
    public void GetFloorHeight_TwoFloors_SecondFloor()
    {
        // verticalDiff = 5.6f → 2 floors, delta = 5.6f
        var result = Building.GetFloorHeight(10f, 15.6f, 1);
        Assert.Equal(15.6f, result);
    }

    [Fact]
    public void GetFloorHeight_ThreeFloors_MiddleFloor()
    {
        // verticalDiff = 11.2f → 3 floors, delta = 5.6f
        var result = Building.GetFloorHeight(10f, 21.2f, 1);
        Assert.Equal(15.6f, result); // 10 + 1 * 5.6
    }

    [Fact]
    public void GetFloorHeight_ThreeFloors_TopFloor()
    {
        // verticalDiff = 11.2f → 3 floors, delta = 5.6f
        var result = Building.GetFloorHeight(10f, 21.2f, 2);
        Assert.Equal(21.2f, result); // 10 + 2 * 5.6
    }

    [Fact]
    public void GetFloorHeight_ThrowsOnNegativeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Building.GetFloorHeight(10f, 20f, -1));
    }

    [Fact]
    public void GetFloorHeight_ThrowsOnIndexBeyondMax()
    {
        // verticalDiff = 5.6f → 2 floors (indices 0, 1). Index 2 should throw.
        Assert.Throws<ArgumentOutOfRangeException>(() => Building.GetFloorHeight(10f, 15.6f, 2));
    }

    [Fact]
    public void GetFloorHeight_LargeBuilding_FloorCount()
    {
        // verticalDiff = 56f → 56/5.6 + 1 = 11 floors
        var result0 = Building.GetFloorHeight(0f, 56f, 0);
        Assert.Equal(0f, result0);

        var result10 = Building.GetFloorHeight(0f, 56f, 10);
        Assert.Equal(56f, result10);
    }

    #endregion

    #region GetFloorHeight(float, float, float) — by reference Z

    [Fact]
    public void GetFloorHeight_ByReferenceZ_SingleFloor_ReturnsMidpoint()
    {
        var result = Building.GetFloorHeight(10f, 15f, 12.5f);
        Assert.Equal(12.5f, result);
    }

    [Fact]
    public void GetFloorHeight_ByReferenceZ_ClosestFloor()
    {
        // 3 floors: 10, 15.6, 21.2
        var result = Building.GetFloorHeight(10f, 21.2f, 14f);
        Assert.Equal(15.6f, result); // 14 is closer to 15.6 than to 10
    }

    [Fact]
    public void GetFloorHeight_ByReferenceZ_ExactlyOnFloor()
    {
        // 2 floors: 10, 15.6
        var result = Building.GetFloorHeight(10f, 15.6f, 10f);
        Assert.Equal(10f, result);
    }

    [Fact]
    public void GetFloorHeight_ByReferenceZ_BelowMin_ClampedToFirst()
    {
        // 2 floors: 10, 15.6
        var result = Building.GetFloorHeight(10f, 15.6f, 0f);
        Assert.Equal(10f, result); // clamped to first floor
    }

    [Fact]
    public void GetFloorHeight_ByReferenceZ_AboveMax_ClampedToLast()
    {
        // 2 floors: 10, 15.6
        var result = Building.GetFloorHeight(10f, 15.6f, 100f);
        Assert.Equal(15.6f, result); // clamped to last floor
    }

    [Fact]
    public void GetFloorHeight_ByReferenceZ_Midpoint_RoundsToNearest()
    {
        // 2 floors: 10, 15.6 → midpoint = 12.8
        var result = Building.GetFloorHeight(10f, 15.6f, 12.8f);
        // (12.8 - 10) / 5.6 = 0.5 → rounds to 0 or 1 depending on Math.Round behavior
        // Math.Round(0.5) = 0 (banker's rounding) in .NET
        Assert.True(result == 10f || result == 15.6f, $"Expected 10 or 15.6, got {result}");
    }

    [Fact]
    public void GetFloorHeight_ByReferenceZ_ThreeFloors_Middle()
    {
        // 3 floors: 10, 15.6, 21.2
        var result = Building.GetFloorHeight(10f, 21.2f, 16f);
        Assert.Equal(15.6f, result);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void GetFloorHeight_ZeroHeightRange_ReturnsSameValue()
    {
        var result = Building.GetFloorHeight(10f, 10f, 0);
        Assert.Equal(10f, result);
    }

    [Fact]
    public void GetFloorHeight_NegativeRange_SingleFloor()
    {
        // minZ > maxZ is unusual but the method doesn't validate order.
        // verticalDiff = |-5| = 5 < 5.6 → 1 floor → midpoint
        var result = Building.GetFloorHeight(10f, 5f, 0);
        Assert.Equal(7.5f, result);
    }

    #endregion
}
