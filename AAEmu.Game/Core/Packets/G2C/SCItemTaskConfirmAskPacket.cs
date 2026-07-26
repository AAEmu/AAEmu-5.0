using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Item-task confirmation request for the 5.0 client.
/// Wire layout from x2game serializer sub_39A21CE0:
/// Item, byte type, 32-byte data, bool encrypt.
/// </summary>
public sealed class SCItemTaskConfirmAskPacket : GamePacket
{
    private readonly Item _item;
    private readonly byte _type;
    private readonly byte[] _data;
    private readonly bool _encrypt;

    public SCItemTaskConfirmAskPacket(Item item, byte type, byte[] data = null, bool encrypt = false)
        : base(SCOffsets.SCItemTaskConfirmAskPacket, 5)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _type = type;
        _data = data ?? new byte[32];
        if (_data.Length != 32)
            throw new ArgumentException("Item-task confirmation data must be exactly 32 bytes.", nameof(data));
        _encrypt = encrypt;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_item);
        stream.Write(_type);
        stream.Write(_data);
        stream.Write(_encrypt);
        return stream;
    }
}
