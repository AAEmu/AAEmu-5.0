using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCDbAttendanceTimePacketTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Write_IncludesPremiumFlagAfterAttendanceTime(bool isArchelife)
    {
        var attendanceTime = DateTime.UnixEpoch.AddSeconds(1_800_000_000);
        var packet = new SCDbAttendanceTimePacket(true, attendanceTime, isArchelife);
        var stream = new PacketStream();

        packet.Write(stream);

        Assert.Equal(10, stream.GetBytes().Length);
        stream.Rollback();
        Assert.True(stream.ReadBoolean());
        Assert.Equal(1_800_000_000L, stream.ReadInt64());
        Assert.Equal(isArchelife, stream.ReadBoolean());
    }
}
