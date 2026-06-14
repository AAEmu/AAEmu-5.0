using System;
using System.Numerics;

using AAEmu.Game.Core.Managers;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Unit tests for <see cref="SpawnHeightDiagnostics"/> — the singleton that stores
/// the last spawn-Z resolution decision for each NPC objId.
/// </summary>
public class SpawnHeightDiagnosticsTests
{
    // SpawnHeightDiagnostics is a singleton; use a short TTL so cleanup tests don't have to wait.
    private readonly SpawnHeightDiagnostics _sut;

    public SpawnHeightDiagnosticsTests()
    {
        _sut = SpawnHeightDiagnostics.Instance;
        _sut.Clear();
        _sut.EntryTtl = TimeSpan.FromMilliseconds(50);
    }

    #region Record / TryGet

    [Fact]
    public void Record_WithValidObjId_StoresEntry()
    {
        var entry = CreateTestEntry(npcObjId: 1, finalZ: 100f);
        _sut.Record(1, entry);

        var found = _sut.TryGet(1, out var retrieved);
        Assert.True(found);
        Assert.NotNull(retrieved);
        Assert.Equal(100f, retrieved.FinalZ);
    }

    [Fact]
    public void Record_WithZeroObjId_DoesNotStore()
    {
        var entry = CreateTestEntry(npcObjId: 0, finalZ: 50f);
        _sut.Record(0, entry);

        var found = _sut.TryGet(0, out _);
        Assert.False(found);
    }

    [Fact]
    public void TryGet_NonExistentObjId_ReturnsFalse()
    {
        var found = _sut.TryGet(99999, out var entry);
        Assert.False(found);
        Assert.Null(entry);
    }

    [Fact]
    public void Record_OverwritesPreviousEntry()
    {
        var entry1 = CreateTestEntry(npcObjId: 42, finalZ: 10f);
        _sut.Record(42, entry1);

        var entry2 = CreateTestEntry(npcObjId: 42, finalZ: 20f);
        _sut.Record(42, entry2);

        var found = _sut.TryGet(42, out var retrieved);
        Assert.True(found);
        Assert.Equal(20f, retrieved!.FinalZ);
    }

    [Fact]
    public void Record_MultipleObjIds_StoresIndependently()
    {
        var entry1 = CreateTestEntry(npcObjId: 1, finalZ: 10f);
        var entry2 = CreateTestEntry(npcObjId: 2, finalZ: 20f);
        _sut.Record(1, entry1);
        _sut.Record(2, entry2);

        _sut.TryGet(1, out var retrieved1);
        _sut.TryGet(2, out var retrieved2);

        Assert.Equal(10f, retrieved1!.FinalZ);
        Assert.Equal(20f, retrieved2!.FinalZ);
    }

    #endregion

    #region Remove

    [Fact]
    public void Remove_ExistingObjId_RemovesEntry()
    {
        var entry = CreateTestEntry(npcObjId: 5, finalZ: 50f);
        _sut.Record(5, entry);
        _sut.Remove(5);

        var found = _sut.TryGet(5, out _);
        Assert.False(found);
    }

    [Fact]
    public void Remove_NonExistentObjId_DoesNotThrow()
    {
        var exception = Record.Exception(() => _sut.Remove(99999));
        Assert.Null(exception);
    }

    #endregion

    #region CleanupOldEntries

    [Fact]
    public void CleanupOldEntries_ExpiredEntries_AreRemoved()
    {
        _sut.EntryTtl = TimeSpan.FromMilliseconds(10);
        var entry = CreateTestEntry(npcObjId: 100, finalZ: 10f);
        _sut.Record(100, entry);

        // Wait for the entry to expire
        System.Threading.Thread.Sleep(50);

        var removed = _sut.CleanupOldEntries();
        Assert.Equal(1, removed);

        var found = _sut.TryGet(100, out _);
        Assert.False(found);
    }

    [Fact]
    public void CleanupOldEntries_FreshEntries_AreKept()
    {
        _sut.EntryTtl = TimeSpan.FromMinutes(30);
        var entry = CreateTestEntry(npcObjId: 200, finalZ: 10f);
        _sut.Record(200, entry);

        var removed = _sut.CleanupOldEntries();
        Assert.Equal(0, removed);

        var found = _sut.TryGet(200, out _);
        Assert.True(found);
    }

    [Fact]
    public void CleanupOldEntries_MixedAge_OnlyExpiredRemoved()
    {
        _sut.EntryTtl = TimeSpan.FromMilliseconds(10);

        var oldEntry = CreateTestEntry(npcObjId: 1, finalZ: 10f);
        _sut.Record(1, oldEntry);

        // Wait for the first entry to expire
        System.Threading.Thread.Sleep(50);

        var freshEntry = CreateTestEntry(npcObjId: 2, finalZ: 20f);
        _sut.Record(2, freshEntry);

        var removed = _sut.CleanupOldEntries();
        Assert.Equal(1, removed);

        var foundOld = _sut.TryGet(1, out _);
        var foundFresh = _sut.TryGet(2, out _);
        Assert.False(foundOld);
        Assert.True(foundFresh);
    }

    [Fact]
    public void CleanupOldEntries_EmptyDictionary_ReturnsZero()
    {
        _sut.Clear();
        var removed = _sut.CleanupOldEntries();
        Assert.Equal(0, removed);
    }

    #endregion

    #region Clear

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        _sut.Record(1, CreateTestEntry(npcObjId: 1, finalZ: 10f));
        _sut.Record(2, CreateTestEntry(npcObjId: 2, finalZ: 20f));
        _sut.Record(3, CreateTestEntry(npcObjId: 3, finalZ: 30f));

        _sut.Clear();

        Assert.False(_sut.TryGet(1, out _));
        Assert.False(_sut.TryGet(2, out _));
        Assert.False(_sut.TryGet(3, out _));
    }

    [Fact]
    public void Clear_EmptyDictionary_DoesNotThrow()
    {
        _sut.Clear();
        var exception = Record.Exception(() => _sut.Clear());
        Assert.Null(exception);
    }

    #endregion

    #region Entry properties

    [Fact]
    public void Entry_AllProperties_CanBeSetAndRetrieved()
    {
        var entry = new SpawnHeightDiagnostics.Entry
        {
            NpcTemplateId = 12345,
            SpawnerTemplateId = 67890,
            SpawnPos = new Vector3(100f, 200f, 300f),
            OriginalSpawnZ = 300f,
            HeightmapZ = 299.5f,
            HasHeightmap = true,
            BaiHasValue = true,
            BaiZ = 301f,
            BaiDistance2D = 2.5f,
            BaiNodeType = 3,
            BaiSource = "test_source",
            FinalZ = 300.5f,
            Decision = "KeepSpawnZ"
        };

        _sut.Record(1, entry);
        _sut.TryGet(1, out var retrieved);

        Assert.NotNull(retrieved);
        Assert.Equal(12345u, retrieved.NpcTemplateId);
        Assert.Equal(67890u, retrieved.SpawnerTemplateId);
        Assert.Equal(new Vector3(100f, 200f, 300f), retrieved.SpawnPos);
        Assert.Equal(300f, retrieved.OriginalSpawnZ);
        Assert.Equal(299.5f, retrieved.HeightmapZ);
        Assert.True(retrieved.HasHeightmap);
        Assert.True(retrieved.BaiHasValue);
        Assert.Equal(301f, retrieved.BaiZ);
        Assert.Equal(2.5f, retrieved.BaiDistance2D);
        Assert.Equal((byte)3, retrieved.BaiNodeType);
        Assert.Equal("test_source", retrieved.BaiSource);
        Assert.Equal(300.5f, retrieved.FinalZ);
        Assert.Equal("KeepSpawnZ", retrieved.Decision);
    }

    [Fact]
    public void Entry_DefaultValues_AreDefault()
    {
        var entry = new SpawnHeightDiagnostics.Entry();

        Assert.Equal(0u, entry.NpcTemplateId);
        Assert.Equal(0u, entry.SpawnerTemplateId);
        Assert.Equal(Vector3.Zero, entry.SpawnPos);
        Assert.Equal(0f, entry.OriginalSpawnZ);
        Assert.Equal(0f, entry.HeightmapZ);
        Assert.False(entry.HasHeightmap);
        Assert.False(entry.BaiHasValue);
        Assert.Equal(0f, entry.BaiZ);
        Assert.Equal(0f, entry.BaiDistance2D);
        Assert.Equal((byte)0, entry.BaiNodeType);
        Assert.Equal(string.Empty, entry.BaiSource);
        Assert.Equal(0f, entry.FinalZ);
        Assert.Equal(string.Empty, entry.Decision);
    }

    #endregion

    #region EntryTtl configuration

    [Fact]
    public void EntryTtl_CanBeSetAndRead()
    {
        // SpawnHeightDiagnostics is a singleton, so we save and restore the original value.
        var original = _sut.EntryTtl;
        try
        {
            _sut.EntryTtl = TimeSpan.FromMinutes(10);
            Assert.Equal(TimeSpan.FromMinutes(10), _sut.EntryTtl);

            _sut.EntryTtl = TimeSpan.FromSeconds(30);
            Assert.Equal(TimeSpan.FromSeconds(30), _sut.EntryTtl);
        }
        finally
        {
            _sut.EntryTtl = original;
        }
    }

    #endregion

    #region Helper methods

    private static SpawnHeightDiagnostics.Entry CreateTestEntry(uint npcObjId, float finalZ)
    {
        return new SpawnHeightDiagnostics.Entry
        {
            NpcTemplateId = npcObjId,
            SpawnerTemplateId = 1,
            SpawnPos = new Vector3(0, 0, finalZ),
            OriginalSpawnZ = finalZ,
            FinalZ = finalZ,
            Decision = "Test"
        };
    }

    #endregion
}
