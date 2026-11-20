using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootDiceSummaryPacket : GamePacket
{
    private readonly LootOwnerType _lootOwnerType;
    private readonly uint _lootOwner;
    private readonly ushort _itemIndex;

    public SCLootDiceSummaryPacket(LootOwnerType lootOwnerType, uint lootOwner, ushort itemIndex, Dictionary<Character, sbyte> diceList)
        : base(SCOffsets.SCLootDiceSummaryPacket, 5)
    {
        _lootOwnerType = lootOwnerType;
        _lootOwner = lootOwner;
        _itemIndex = itemIndex;
        _ = diceList; // The current packet layout intentionally omits the dice list.
    }

    public override PacketStream Write(PacketStream stream)
    {
        #region iid
        stream.Write(_itemIndex);
        stream.Write((ushort)_lootOwnerType);
        stream.WriteBc(_lootOwner);
        stream.Write((byte)0);
        #endregion
        //stream.Write(diceList.Count);
        //foreach (var (player, dice) in diceList)
        //{
        //    stream.Write(player.Id);
        //    stream.Write(dice);
        //}
        return stream;
    }
}
