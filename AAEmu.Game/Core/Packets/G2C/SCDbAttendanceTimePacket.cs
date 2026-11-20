using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDbAttendanceTimePacket : GamePacket
{
    private readonly bool _result;
    private readonly DateTime _dbAttendanceTime;
    private readonly bool _isArchelife;

    public SCDbAttendanceTimePacket(bool result, DateTime dbAttendanceTime, bool isArchelife) : base(SCOffsets.SCDbAttendanceTimePacket, 5)
    {
        _result = result;
        _dbAttendanceTime = dbAttendanceTime;
        _isArchelife = isArchelife;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_result);
        stream.Write(_dbAttendanceTime);
        stream.Write(_isArchelife);

        return stream;
    }
}
