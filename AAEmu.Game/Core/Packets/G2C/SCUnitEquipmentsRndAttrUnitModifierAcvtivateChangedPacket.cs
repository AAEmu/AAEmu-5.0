using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Updates the equipment slots whose random/synthesis modifiers are active.
/// The 5.0 client serializer stores 34 boolean slot flags in one 64-bit field.
/// </summary>
public sealed class SCUnitEquipmentsRndAttrUnitModifierAcvtivateChangedPacket : GamePacket
{
    private const int ClientEquipmentFlagCount = 34;
    private const int ModifierSlotStart = 13;
    private const int ModifierSlotCount = 5;

    private readonly uint _objId;
    private readonly ulong _activeEquipmentFlags;

    public SCUnitEquipmentsRndAttrUnitModifierAcvtivateChangedPacket(Unit unit)
        : base(SCOffsets.SCUnitEquipmentsRndAttrUnitModifierAcvtivateChangedPacket, 5)
    {
        ArgumentNullException.ThrowIfNull(unit);

        _objId = unit.ObjId;
        ulong flags = 0;

        for (var equipmentSlot = 0; equipmentSlot < ClientEquipmentFlagCount; equipmentSlot++)
        {
            if (unit.Equipment.GetItemBySlot(equipmentSlot) is not EquipItem equipItem)
                continue;

            var modifierSlotEnd = Math.Min(equipItem.GemIds.Length, ModifierSlotStart + ModifierSlotCount);
            for (var modifierSlot = ModifierSlotStart; modifierSlot < modifierSlotEnd; modifierSlot++)
            {
                if (equipItem.GemIds[modifierSlot] == 0)
                    continue;

                flags |= 1UL << equipmentSlot;
                break;
            }
        }

        _activeEquipmentFlags = flags;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objId);
        stream.Write(_activeEquipmentFlags);
        return stream;
    }
}
