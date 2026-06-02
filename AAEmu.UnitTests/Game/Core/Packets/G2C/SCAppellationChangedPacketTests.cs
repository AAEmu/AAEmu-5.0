using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCAppellationChangedPacketTests
{
    [Fact]
    public void Write_ShouldSerializeObjIdAppellationIdAndBuffId()
    {
        const uint objId = 1u;
        const uint appellationId = 42u;
        const uint buffId = 99u;
        var packet = new SCAppellationChangedPacket(objId, appellationId, buffId);
        var stream = new PacketStream();

        packet.Write(stream);
        stream.Rollback();

        Assert.Equal(objId, stream.ReadBc());
        Assert.Equal(appellationId, stream.ReadUInt32());
        Assert.Equal(buffId, stream.ReadUInt32());
    }

    [Fact]
    public void Write_WithZeroBuffId_ShouldWriteZero()
    {
        const uint objId = 5u;
        const uint appellationId = 10u;
        const uint buffId = 0u;
        var packet = new SCAppellationChangedPacket(objId, appellationId, buffId);
        var stream = new PacketStream();

        packet.Write(stream);
        stream.Rollback();

        stream.ReadBc();     // skip objId
        stream.ReadUInt32(); // skip appellationId
        Assert.Equal(0u, stream.ReadUInt32());
    }

    [Fact]
    public void Write_WithMaxBuffId_ShouldSerializeCorrectly()
    {
        const uint buffId = uint.MaxValue;
        var packet = new SCAppellationChangedPacket(1u, 1u, buffId);
        var stream = new PacketStream();

        packet.Write(stream);
        stream.Rollback();

        stream.ReadBc();
        stream.ReadUInt32();
        Assert.Equal(buffId, stream.ReadUInt32());
    }
}
