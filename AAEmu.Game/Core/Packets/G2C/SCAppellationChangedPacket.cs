using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAppellationChangedPacket : GamePacket
{
    private readonly uint _objId;
    private readonly uint _appellationId;
    private readonly uint _appellationBuffId;

    public SCAppellationChangedPacket(uint objId, uint appellationId, uint appellationBuffId) : base(SCOffsets.SCAppellationChangedPacket, 5)
    {
        _objId = objId;
        _appellationId = appellationId;
        _appellationBuffId = appellationBuffId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objId);
        stream.Write(_appellationId);
        stream.Write(_appellationBuffId);
        return stream;
    }
}
