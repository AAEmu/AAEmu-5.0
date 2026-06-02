using System.Collections.Generic;
using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Units;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class CharacterAppellationsTests
{
    private class CapturingCharacterMock : Character
    {
        public GamePacket LastBroadcastedPacket { get; private set; }

        public CapturingCharacterMock() : base(null) { }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            LastBroadcastedPacket = packet;
        }
    }

    private static void SeedAppellationTemplate(uint id, uint buffId)
    {
        var manager = CharacterManager.Instance;
        var field = typeof(CharacterManager)
            .GetField("_appellations", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (Dictionary<uint, AppellationTemplate>)field.GetValue(manager);
        dict[id] = new AppellationTemplate { Id = id, BuffId = buffId };
    }

    private static uint ReadBuffIdFromPacket(GamePacket packet)
    {
        var stream = new PacketStream();
        packet.Write(stream);
        stream.Rollback();
        stream.ReadBc();     // objId
        stream.ReadUInt32(); // appellationId
        return stream.ReadUInt32();
    }

    [Fact]
    public void Change_ToKnownAppellation_BroadcastsPacketWithCorrectBuffId()
    {
        const uint titleId = 10001u;
        const uint expectedBuffId = 777u;
        SeedAppellationTemplate(titleId, expectedBuffId);

        var owner = new CapturingCharacterMock();
        owner.Buffs = new Mock<IBuffs>().Object;
        var appellations = new CharacterAppellations(owner);
        appellations.Appellations.Add(titleId);

        appellations.Change(titleId);

        Assert.NotNull(owner.LastBroadcastedPacket);
        Assert.Equal(expectedBuffId, ReadBuffIdFromPacket(owner.LastBroadcastedPacket));
    }

    [Fact]
    public void Change_ToZero_BroadcastsPacketWithZeroBuffId()
    {
        var owner = new CapturingCharacterMock();
        owner.Buffs = new Mock<IBuffs>().Object;
        var appellations = new CharacterAppellations(owner);

        appellations.Change(0);

        Assert.NotNull(owner.LastBroadcastedPacket);
        Assert.Equal(0u, ReadBuffIdFromPacket(owner.LastBroadcastedPacket));
    }

    [Fact]
    public void Change_ToUnknownId_LeavesActiveAppellationUnchanged()
    {
        const uint unknownId = 99999u;
        var owner = new CapturingCharacterMock();
        owner.Buffs = new Mock<IBuffs>().Object;
        var appellations = new CharacterAppellations(owner);
        // unknownId is NOT added to Appellations — should be ignored

        appellations.Change(unknownId);

        Assert.NotNull(owner.LastBroadcastedPacket);
        var stream = new PacketStream();
        owner.LastBroadcastedPacket.Write(stream);
        stream.Rollback();
        stream.ReadBc();
        Assert.Equal(0u, stream.ReadUInt32()); // ActiveAppellation unchanged — still 0
    }
}
