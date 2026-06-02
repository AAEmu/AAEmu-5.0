# Appellation BuffId in SCAppellationChanged packet (2026-06-02)

## The bug

When a character changed their active appellation (title), nearby clients did
not receive the associated buff ID in `SCAppellationChangedPacket`.  The client
protocol (5.0.7.0) expects `buffId` as the **third** serialized field — without
it, client-side title effects (visual buffs, stat overlays) were ignored on
remote clients.

---

## Root cause

`SCAppellationChangedPacket.Write()` only serialized two fields:

```
WriteBc(_objId)
Write(_appellationId)
```

The client expected a third `uint32` — `buffId`.  The server already knew the
correct value: `CharacterAppellations.addBuff()` called
`CharacterManager.Instance.GetAppellationsTemplate(id)?.BuffId`, but this
value was never forwarded to the broadcast packet.

---

## The fix

### 1. `SCAppellationChangedPacket` — added `buffId` parameter and field

```csharp
// Before
public SCAppellationChangedPacket(uint objId, uint appellationId)

// After
public SCAppellationChangedPacket(uint objId, uint appellationId, uint buffId)
```

`Write()` now serializes all three fields:

```csharp
stream.WriteBc(_objId);
stream.Write(_appellationId);
stream.Write(_buffId);          // new
```

### 2. `CharacterAppellations.Change()` — resolves and forwards `buffId`

```csharp
// Before
Owner.BroadcastPacket(new SCAppellationChangedPacket(Owner.ObjId, ActiveAppellation), true);

// After
var buffId = CharacterManager.Instance.GetAppellationsTemplate(ActiveAppellation)?.BuffId ?? 0;
Owner.BroadcastPacket(new SCAppellationChangedPacket(Owner.ObjId, ActiveAppellation, buffId), true);
```

`ActiveAppellation` is already updated before this line, so:
- title cleared (`id == 0`) → `ActiveAppellation = 0` → `buffId = 0`
- valid title set → `ActiveAppellation = id` → `buffId` from template
- unknown id (not in Appellations) → `ActiveAppellation` unchanged → `buffId`
  of the previously active title (or `0` if none)

---

## Files changed

| File | Change |
|------|--------|
| `AAEmu.Game/Core/Packets/G2C/SCAppellationChangedPacket.cs` | Added `_buffId` field, extended constructor, added `stream.Write(_buffId)` |
| `AAEmu.Game/Models/Game/Char/CharacterAppellations.cs` | Resolve `buffId` from template before broadcasting |

## Tests added

**`AAEmu.UnitTests/Game/Core/Packets/G2C/SCAppellationChangedPacketTests.cs`**

| Test | What it verifies |
|------|-----------------|
| `Write_ShouldSerializeObjIdAppellationIdAndBuffId` | All three fields round-trip correctly through `PacketStream` |
| `Write_WithZeroBuffId_ShouldWriteZero` | `buffId = 0` is written (not omitted) |
| `Write_WithMaxBuffId_ShouldSerializeCorrectly` | `uint.MaxValue` serializes without truncation |

**`AAEmu.UnitTests/Game/Models/Game/Char/CharacterAppellationsTests.cs`**

| Test | What it verifies |
|------|-----------------|
| `Change_ToKnownAppellation_BroadcastsPacketWithCorrectBuffId` | Known appellation → packet carries the template's `BuffId` |
| `Change_ToZero_BroadcastsPacketWithZeroBuffId` | Clearing title → `buffId = 0` in packet |
| `Change_ToUnknownId_LeavesActiveAppellationUnchanged` | Unknown id → `ActiveAppellation` unchanged, packet still sent |
