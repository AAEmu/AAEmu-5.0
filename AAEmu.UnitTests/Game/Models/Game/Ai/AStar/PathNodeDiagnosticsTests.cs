using System;
using System.Numerics;

using AAEmu.Game.Models.Game.AI.AStar;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.AI.AStar;

/// <summary>
/// Unit tests for the diagnostic fields added to <see cref="PathNode"/> in v6.
/// These fields are used by the /pathmob GM command and for logging.
/// </summary>
public class PathNodeDiagnosticsTests
{
    #region Default values

    [Fact]
    public void PathNode_LastStatus_DefaultIsNone()
    {
        var pn = new PathNode();
        Assert.Equal(PathStatus.None, pn.LastStatus);
    }

    [Fact]
    public void PathNode_LastStartReal_DefaultIsZero()
    {
        var pn = new PathNode();
        Assert.Equal(Vector3.Zero, pn.LastStartReal);
    }

    [Fact]
    public void PathNode_LastEndReal_DefaultIsZero()
    {
        var pn = new PathNode();
        Assert.Equal(Vector3.Zero, pn.LastEndReal);
    }

    [Fact]
    public void PathNode_LastStartNodePos_DefaultIsZero()
    {
        var pn = new PathNode();
        Assert.Equal(Vector3.Zero, pn.LastStartNodePos);
    }

    [Fact]
    public void PathNode_LastEndNodePos_DefaultIsZero()
    {
        var pn = new PathNode();
        Assert.Equal(Vector3.Zero, pn.LastEndNodePos);
    }

    [Fact]
    public void PathNode_LastRawNodeCount_DefaultIsZero()
    {
        var pn = new PathNode();
        Assert.Equal(0, pn.LastRawNodeCount);
    }

    [Fact]
    public void PathNode_LastPathLength_DefaultIsZero()
    {
        var pn = new PathNode();
        Assert.Equal(0f, pn.LastPathLength);
    }

    [Fact]
    public void PathNode_LastOptimalDistance_DefaultIsZero()
    {
        var pn = new PathNode();
        Assert.Equal(0f, pn.LastOptimalDistance);
    }

    [Fact]
    public void PathNode_LastStartNodeSkipped_DefaultIsFalse()
    {
        var pn = new PathNode();
        Assert.False(pn.LastStartNodeSkipped);
    }

    [Fact]
    public void PathNode_LastRepathReason_DefaultIsEmptyString()
    {
        var pn = new PathNode();
        Assert.Equal(string.Empty, pn.LastRepathReason);
    }

    [Fact]
    public void PathNode_LastRepathTime_DefaultIsMinValue()
    {
        var pn = new PathNode();
        Assert.Equal(DateTime.MinValue, pn.LastRepathTime);
    }

    [Fact]
    public void PathNode_LastTargetObjId_DefaultIsZero()
    {
        var pn = new PathNode();
        Assert.Equal(0u, pn.LastTargetObjId);
    }

    [Fact]
    public void PathNode_LastTargetPosAtRepath_DefaultIsZero()
    {
        var pn = new PathNode();
        Assert.Equal(Vector3.Zero, pn.LastTargetPosAtRepath);
    }

    #endregion

    #region Round-trip values

    [Fact]
    public void PathNode_Diagnostics_RoundTrip()
    {
        var pn = new PathNode
        {
            LastStatus = PathStatus.Complete,
            LastStartReal = new Vector3(1, 2, 3),
            LastEndReal = new Vector3(4, 5, 6),
            LastStartNodePos = new Vector3(1.5f, 2.5f, 3.5f),
            LastEndNodePos = new Vector3(4.5f, 5.5f, 6.5f),
            LastRawNodeCount = 42,
            LastPathLength = 123.45f,
            LastOptimalDistance = 100f,
            LastStartNodeSkipped = true,
            LastRepathReason = "TargetMoved",
            LastRepathTime = DateTime.UtcNow,
            LastTargetObjId = 999,
            LastTargetPosAtRepath = new Vector3(7, 8, 9)
        };

        Assert.Equal(PathStatus.Complete, pn.LastStatus);
        Assert.Equal(new Vector3(1, 2, 3), pn.LastStartReal);
        Assert.Equal(new Vector3(4, 5, 6), pn.LastEndReal);
        Assert.Equal(new Vector3(1.5f, 2.5f, 3.5f), pn.LastStartNodePos);
        Assert.Equal(new Vector3(4.5f, 5.5f, 6.5f), pn.LastEndNodePos);
        Assert.Equal(42, pn.LastRawNodeCount);
        Assert.Equal(123.45f, pn.LastPathLength);
        Assert.Equal(100f, pn.LastOptimalDistance);
        Assert.True(pn.LastStartNodeSkipped);
        Assert.Equal("TargetMoved", pn.LastRepathReason);
        Assert.Equal(DateTime.UtcNow, pn.LastRepathTime, TimeSpan.FromSeconds(1));
        Assert.Equal(999u, pn.LastTargetObjId);
        Assert.Equal(new Vector3(7, 8, 9), pn.LastTargetPosAtRepath);
    }

    #endregion

    #region PathStatus enum

    [Fact]
    public void PathStatus_HasAllExpectedValues()
    {
        Assert.Equal(PathStatus.None, (PathStatus)0);
        Assert.Equal(PathStatus.Complete, (PathStatus)1);
        Assert.Equal(PathStatus.Partial, (PathStatus)2);
        Assert.Equal(PathStatus.Failed, (PathStatus)3);
        Assert.Equal(PathStatus.DirectFallback, (PathStatus)4);
    }

    #endregion

    #region EstimateFullPathLength

    [Fact]
    public void PathNode_EstimateFullPathLength_IsSumOfStartAndEnd()
    {
        var pn = new PathNode
        {
            PathLengthFromStart = 10f,
            PathLengthToEnd = 20f
        };
        Assert.Equal(30f, pn.EstimateFullPathLength);
    }

    #endregion

    #region FoundPath

    [Fact]
    public void PathNode_FoundPath_DefaultIsNotNull()
    {
        var pn = new PathNode();
        Assert.NotNull(pn.FoundPath);
        Assert.Empty(pn.FoundPath);
    }

    #endregion
}
