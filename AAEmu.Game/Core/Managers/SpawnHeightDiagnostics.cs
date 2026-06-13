using System;
using System.Collections.Concurrent;
using System.Linq;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Singleton that stores the last spawn-Z resolution decision for each NPC objId.
/// Consumed by the /heightmob GM command to show what was decided at spawn time,
/// independently of where the NPC has moved since.
///
/// Memory management: entries are removed when <see cref="Npc.Delete"/> calls <see cref="Remove"/>.
/// As a safety net, <see cref="CleanupOldEntries"/> can be called periodically (e.g., from a
/// server-tick handler) to evict entries older than the configured TTL. Call <see cref="Clear"/>
/// on server shutdown to release all references.
/// </summary>
public sealed class SpawnHeightDiagnostics
{
    private static readonly Lazy<SpawnHeightDiagnostics> _instance = new(() => new SpawnHeightDiagnostics());
    public static SpawnHeightDiagnostics Instance => _instance.Value;

    private readonly ConcurrentDictionary<uint, TimedEntry> _entries = new();

    /// <summary>
    /// Default time-to-live for diagnostic entries. Entries older than this are eligible for
    /// cleanup. Configurable for testing; defaults to 30 minutes.
    /// </summary>
    public TimeSpan EntryTtl { get; set; } = TimeSpan.FromMinutes(30);

    private SpawnHeightDiagnostics() { }

    public void Record(uint npcObjId, Entry entry)
    {
        if (npcObjId == 0)
            return;
        _entries[npcObjId] = new TimedEntry { Entry = entry, Timestamp = DateTime.UtcNow };
    }

    public bool TryGet(uint npcObjId, out Entry entry)
    {
        if (_entries.TryGetValue(npcObjId, out var timed))
        {
            entry = timed.Entry;
            return true;
        }
        entry = null;
        return false;
    }

    public void Remove(uint npcObjId)
    {
        _entries.TryRemove(npcObjId, out _);
    }

    /// <summary>
    /// Removes all entries whose timestamp is older than <see cref="EntryTtl"/>.
    /// Safe to call from a periodic tick handler. Returns the number of entries removed.
    /// </summary>
    public int CleanupOldEntries()
    {
        var cutoff = DateTime.UtcNow - EntryTtl;
        var removed = 0;
        foreach (var kvp in _entries)
        {
            if (kvp.Value.Timestamp < cutoff && _entries.TryRemove(kvp.Key, out _))
                removed++;
        }
        return removed;
    }

    /// <summary>
    /// Removes all entries. Call on server shutdown to release references.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    private class TimedEntry
    {
        public Entry Entry { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class Entry
    {
        public uint NpcTemplateId { get; set; }
        public uint SpawnerTemplateId { get; set; }
        public System.Numerics.Vector3 SpawnPos { get; set; }
        public float OriginalSpawnZ { get; set; }
        public float HeightmapZ { get; set; }
        public bool HasHeightmap { get; set; }
        public bool BaiHasValue { get; set; }
        public float BaiZ { get; set; }
        public float BaiDistance2D { get; set; }
        public byte BaiNodeType { get; set; }
        public string BaiSource { get; set; } = string.Empty;
        public float FinalZ { get; set; }
        public string Decision { get; set; } = string.Empty;
    }
}
