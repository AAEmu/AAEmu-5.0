using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Models.Game.AI.AStar;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.AI.AStar;

/// <summary>
/// Unit tests for <see cref="AStarHeap"/> — the binary min-heap used by the v6 optimized A*.
/// </summary>
public class AStarHeapTests
{
    #region Basic operations

    [Fact]
    public void NewHeap_CountIsZero()
    {
        var heap = new AStarHeap();
        Assert.Equal(0, heap.Count);
    }

    [Fact]
    public void Push_SingleElement_CountIsOne()
    {
        var heap = new AStarHeap();
        heap.Push(new AStarEntry(Vector3.Zero, 0f, 10f, 0));
        Assert.Equal(1, heap.Count);
    }

    [Fact]
    public void Pop_SingleElement_ReturnsThatElement()
    {
        var heap = new AStarHeap();
        var entry = new AStarEntry(new Vector3(1, 2, 3), 5f, 10f, 0);
        heap.Push(entry);

        var result = heap.Pop();
        Assert.Equal(entry.Pos, result.Pos);
        Assert.Equal(entry.G, result.G);
        Assert.Equal(entry.H, result.H);
        Assert.Equal(0, heap.Count);
    }

    [Fact]
    public void Pop_EmptyHeap_Throws()
    {
        var heap = new AStarHeap();
        Assert.Throws<IndexOutOfRangeException>(() => heap.Pop());
    }

    #endregion

    #region Ordering (min-heap by F, then H, then Seq)

    [Fact]
    public void Pop_TwoElements_LowerFPoppedFirst()
    {
        var heap = new AStarHeap();
        // F = G + H
        var highF = new AStarEntry(Vector3.Zero, 10f, 10f, 0); // F=20
        var lowF = new AStarEntry(Vector3.Zero, 5f, 5f, 1);    // F=10

        heap.Push(highF);
        heap.Push(lowF);

        var first = heap.Pop();
        Assert.Equal(10f, first.F);

        var second = heap.Pop();
        Assert.Equal(20f, second.F);
    }

    [Fact]
    public void Pop_SameF_LowerHPoppedFirst()
    {
        var heap = new AStarHeap();
        // Both F=20, but H differs
        var highH = new AStarEntry(Vector3.Zero, 5f, 15f, 0); // F=20, H=15
        var lowH = new AStarEntry(Vector3.Zero, 10f, 10f, 1); // F=20, H=10

        heap.Push(highH);
        heap.Push(lowH);

        var first = heap.Pop();
        Assert.Equal(10f, first.H); // Lower H first

        var second = heap.Pop();
        Assert.Equal(15f, second.H);
    }

    [Fact]
    public void Pop_SameF_SameH_LowerSeqPoppedFirst()
    {
        var heap = new AStarHeap();
        // Both F=20, H=10, but Seq differs
        var later = new AStarEntry(Vector3.Zero, 10f, 10f, 5);
        var earlier = new AStarEntry(Vector3.Zero, 10f, 10f, 2);

        heap.Push(later);
        heap.Push(earlier);

        var first = heap.Pop();
        Assert.Equal(2L, first.Seq); // Lower seq first

        var second = heap.Pop();
        Assert.Equal(5L, second.Seq);
    }

    [Fact]
    public void Pop_MultipleElements_ReturnsInSortedOrder()
    {
        var heap = new AStarHeap();
        var random = new Random(42);
        var entries = new List<AStarEntry>();

        for (var i = 0; i < 20; i++)
        {
            var g = (float)random.NextDouble() * 100;
            var h = (float)random.NextDouble() * 100;
            var entry = new AStarEntry(Vector3.Zero, g, h, i);
            entries.Add(entry);
            heap.Push(entry);
        }

        // Expected order: sort by F, then H, then Seq
        var expected = entries
            .OrderBy(e => e.F)
            .ThenBy(e => e.H)
            .ThenBy(e => e.Seq)
            .ToList();

        var actual = new List<AStarEntry>();
        while (heap.Count > 0)
        {
            actual.Add(heap.Pop());
        }

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].F, actual[i].F);
            Assert.Equal(expected[i].H, actual[i].H);
            Assert.Equal(expected[i].Seq, actual[i].Seq);
        }
    }

    #endregion

    #region Clear

    [Fact]
    public void Clear_ResetsCount()
    {
        var heap = new AStarHeap();
        heap.Push(new AStarEntry(Vector3.Zero, 0f, 10f, 0));
        heap.Push(new AStarEntry(Vector3.Zero, 5f, 5f, 1));

        heap.Clear();
        Assert.Equal(0, heap.Count);
    }

    [Fact]
    public void Clear_ThenPush_WorksCorrectly()
    {
        var heap = new AStarHeap();
        heap.Push(new AStarEntry(Vector3.Zero, 0f, 10f, 0));
        heap.Clear();
        heap.Push(new AStarEntry(Vector3.Zero, 5f, 5f, 1));

        Assert.Equal(1, heap.Count);
        var result = heap.Pop();
        Assert.Equal(10f, result.F);
    }

    #endregion

    #region Dynamic resizing

    [Fact]
    public void Push_ManyElements_ResizesCorrectly()
    {
        var heap = new AStarHeap();
        // Default capacity is 64, push more to trigger resize
        for (var i = 0; i < 100; i++)
        {
            heap.Push(new AStarEntry(Vector3.Zero, (float)i, 0f, i));
        }

        Assert.Equal(100, heap.Count);

        // Verify they come out in order
        var prevF = -1f;
        while (heap.Count > 0)
        {
            var entry = heap.Pop();
            Assert.True(entry.F >= prevF, $"Heap ordering violated: {entry.F} < {prevF}");
            prevF = entry.F;
        }
    }

    #endregion

    #region AStarEntry struct tests

    [Fact]
    public void AStarEntry_F_IsSumOfGAndH()
    {
        var entry = new AStarEntry(Vector3.Zero, 3f, 7f, 0);
        Assert.Equal(10f, entry.F);
    }

    [Fact]
    public void AStarEntry_Properties_RoundTrip()
    {
        var pos = new Vector3(1f, 2f, 3f);
        var entry = new AStarEntry(pos, 5f, 10f, 42);

        Assert.Equal(pos, entry.Pos);
        Assert.Equal(5f, entry.G);
        Assert.Equal(10f, entry.H);
        Assert.Equal(15f, entry.F);
        Assert.Equal(42L, entry.Seq);
    }

    [Fact]
    public void AStarEntry_CompareTo_LowerFLessThan()
    {
        var a = new AStarEntry(Vector3.Zero, 5f, 5f, 0);  // F=10
        var b = new AStarEntry(Vector3.Zero, 10f, 10f, 1); // F=20

        var result = a.CompareTo(b);
        Assert.True(result < 0, "Lower F should compare as less than higher F");
    }

    [Fact]
    public void AStarEntry_CompareTo_HigherFGreaterThan()
    {
        var a = new AStarEntry(Vector3.Zero, 10f, 10f, 0); // F=20
        var b = new AStarEntry(Vector3.Zero, 5f, 5f, 1);   // F=10

        var result = a.CompareTo(b);
        Assert.True(result > 0, "Higher F should compare as greater than lower F");
    }

    [Fact]
    public void AStarEntry_CompareTo_SameF_LowerHLessThan()
    {
        var a = new AStarEntry(Vector3.Zero, 10f, 5f, 0);  // F=15, H=5
        var b = new AStarEntry(Vector3.Zero, 5f, 10f, 1);  // F=15, H=10

        var result = a.CompareTo(b);
        Assert.True(result < 0, "Same F, lower H should compare as less");
    }

    [Fact]
    public void AStarEntry_CompareTo_SameF_SameH_LowerSeqLessThan()
    {
        var a = new AStarEntry(Vector3.Zero, 5f, 10f, 3);  // F=15, H=10, Seq=3
        var b = new AStarEntry(Vector3.Zero, 5f, 10f, 7);  // F=15, H=10, Seq=7

        var result = a.CompareTo(b);
        Assert.True(result < 0, "Same F, same H, lower seq should compare as less");
    }

    [Fact]
    public void AStarEntry_CompareTo_AllEqual_ReturnsZero()
    {
        var a = new AStarEntry(Vector3.Zero, 5f, 10f, 3);
        var b = new AStarEntry(Vector3.Zero, 5f, 10f, 3);

        var result = a.CompareTo(b);
        Assert.Equal(0, result);
    }

    #endregion
}
