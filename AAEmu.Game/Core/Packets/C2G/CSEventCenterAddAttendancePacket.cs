using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSEventCenterAddAttendancePacket : GamePacket
{
    public CSEventCenterAddAttendancePacket() : base(CSOffsets.CSEventCenterAddAttendancePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var characterId = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character == null || character.Id != characterId || character.AccountId != Connection.AccountId)
            return;

        character.Attendances?.RegisterDailyAttendance();
    }
}
