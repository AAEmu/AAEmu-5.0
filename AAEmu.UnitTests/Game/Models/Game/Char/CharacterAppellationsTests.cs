using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Skills.Templates;
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

    private static void AddLoadedAppellation(CharacterAppellations appellations, uint id, uint buffId)
    {
        appellations.Appellations.Add(id);

        var field = typeof(CharacterAppellations)
            .GetField("_appellationBuffIds", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (Dictionary<uint, uint>)field.GetValue(appellations);
        dict[id] = buffId;
    }

    private static void SeedBuffTemplate(uint buffId)
    {
        var manager = SkillManager.Instance;
        var field = typeof(SkillManager)
            .GetField("_buffs", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (Dictionary<uint, BuffTemplate>)field.GetValue(manager);
        if (dict == null)
        {
            dict = new Dictionary<uint, BuffTemplate>();
            field.SetValue(manager, dict);
        }

        dict[buffId] = new BuffTemplate { Id = buffId };
    }

    private static void SeedPassiveBuffTemplate(uint passiveBuffId, uint effectBuffId)
    {
        var manager = SkillManager.Instance;
        var field = typeof(SkillManager)
            .GetField("_passiveBuffs", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (Dictionary<uint, PassiveBuffTemplate>)field.GetValue(manager);
        if (dict == null)
        {
            dict = new Dictionary<uint, PassiveBuffTemplate>();
            field.SetValue(manager, dict);
        }

        dict[passiveBuffId] = new PassiveBuffTemplate { Id = passiveBuffId, BuffId = effectBuffId };
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
    public void Change_ToKnownAppellation_AppliesProvidedAvailableBuffId()
    {
        const uint titleId = 10001u;
        const uint expectedBuffId = 777u;
        SeedAppellationTemplate(titleId, expectedBuffId);
        SeedBuffTemplate(expectedBuffId);

        var buffsMock = new Mock<IBuffs>();
        var owner = new CapturingCharacterMock();
        owner.Buffs = buffsMock.Object;
        var appellations = new CharacterAppellations(owner);
        appellations.Appellations.Add(titleId);

        appellations.Change(titleId, expectedBuffId);

        Assert.NotNull(owner.LastBroadcastedPacket);
        Assert.Equal(expectedBuffId, ReadBuffIdFromPacket(owner.LastBroadcastedPacket));
        buffsMock.Verify(b => b.AddBuff(expectedBuffId, owner), Times.Once);
    }

    [Fact]
    public void Change_UsesLoadedDbBuffIdWithoutTemplate()
    {
        const uint titleId = 19001u;
        const uint dbBuffId = 19001u;
        SeedBuffTemplate(dbBuffId);

        var buffsMock = new Mock<IBuffs>();
        var owner = new CapturingCharacterMock();
        owner.Buffs = buffsMock.Object;
        var appellations = new CharacterAppellations(owner);
        AddLoadedAppellation(appellations, titleId, dbBuffId);

        appellations.Change(titleId, dbBuffId);

        Assert.NotNull(owner.LastBroadcastedPacket);
        Assert.Equal(dbBuffId, ReadBuffIdFromPacket(owner.LastBroadcastedPacket));
        buffsMock.Verify(b => b.AddBuff(dbBuffId, owner), Times.Once);
    }

    [Fact]
    public void Change_ToKnownAppellation_AllowsBuffFromAnotherOwnedAppellation()
    {
        const uint displayedTitleId = 10001u;
        const uint buffTitleId = 10002u;
        const uint selectedBuffId = 888u;
        SeedAppellationTemplate(displayedTitleId, 777u);
        SeedAppellationTemplate(buffTitleId, selectedBuffId);
        SeedBuffTemplate(selectedBuffId);

        var buffsMock = new Mock<IBuffs>();
        var owner = new CapturingCharacterMock();
        owner.Buffs = buffsMock.Object;
        var appellations = new CharacterAppellations(owner);
        appellations.Appellations.Add(displayedTitleId);
        appellations.Appellations.Add(buffTitleId);

        appellations.Change(displayedTitleId, selectedBuffId);

        Assert.NotNull(owner.LastBroadcastedPacket);
        Assert.Equal(selectedBuffId, ReadBuffIdFromPacket(owner.LastBroadcastedPacket));
        buffsMock.Verify(b => b.AddBuff(selectedBuffId, owner), Times.Once);
    }

    [Fact]
    public void Change_WithUnavailableBuff_LeavesActiveAppellationUnchanged()
    {
        const uint titleId = 10001u;
        const uint currentBuffId = 777u;
        const uint unavailableBuffId = 999u;
        SeedAppellationTemplate(titleId, currentBuffId);
        SeedBuffTemplate(currentBuffId);

        var buffsMock = new Mock<IBuffs>();
        var owner = new CapturingCharacterMock();
        owner.Buffs = buffsMock.Object;
        var appellations = new CharacterAppellations(owner)
        {
            ActiveAppellation = titleId,
            ActiveAppellationBuffId = currentBuffId
        };
        appellations.Appellations.Add(titleId);

        appellations.Change(titleId, unavailableBuffId);

        Assert.NotNull(owner.LastBroadcastedPacket);
        Assert.Equal(currentBuffId, ReadBuffIdFromPacket(owner.LastBroadcastedPacket));
        buffsMock.Verify(b => b.RemoveBuff(It.IsAny<uint>()), Times.Never);
        buffsMock.Verify(b => b.AddBuff(It.IsAny<uint>(), It.IsAny<BaseUnit>()), Times.Never);
    }

    [Fact]
    public void ApplyActiveBuff_UsesLoadedActiveBuffId()
    {
        const uint loadedBuffId = 19002u;
        SeedBuffTemplate(loadedBuffId);

        var buffsMock = new Mock<IBuffs>();
        var owner = new CapturingCharacterMock();
        owner.Buffs = buffsMock.Object;
        var appellations = new CharacterAppellations(owner)
        {
            ActiveAppellation = 19u,
            ActiveAppellationBuffId = loadedBuffId
        };

        appellations.ApplyActiveBuff();

        buffsMock.Verify(b => b.AddBuff(loadedBuffId, owner), Times.Once);
    }

    [Fact]
    public void ApplyActiveBuff_ResolvesPassiveBuffIdToEffectBuffId()
    {
        const uint selectedBuffId = 19u;
        const uint effectBuffId = 558u;
        SeedPassiveBuffTemplate(selectedBuffId, effectBuffId);
        SeedBuffTemplate(effectBuffId);

        var buffsMock = new Mock<IBuffs>();
        var owner = new CapturingCharacterMock();
        owner.Buffs = buffsMock.Object;
        var appellations = new CharacterAppellations(owner)
        {
            ActiveAppellation = 19u,
            ActiveAppellationBuffId = selectedBuffId
        };

        appellations.ApplyActiveBuff();

        buffsMock.Verify(b => b.AddBuff(effectBuffId, owner), Times.Once);
    }

    [Fact]
    public void Change_ToZero_BroadcastsPacketWithZeroBuffId()
    {
        const uint currentBuffId = 19003u;
        SeedBuffTemplate(currentBuffId);
        var owner = new CapturingCharacterMock();
        var buffsMock = new Mock<IBuffs>();
        owner.Buffs = buffsMock.Object;
        var appellations = new CharacterAppellations(owner)
        {
            ActiveAppellation = 19u,
            ActiveAppellationBuffId = currentBuffId
        };

        appellations.Change(0, 0);

        Assert.NotNull(owner.LastBroadcastedPacket);
        Assert.Equal(0u, ReadBuffIdFromPacket(owner.LastBroadcastedPacket));
        buffsMock.Verify(b => b.RemoveBuff(currentBuffId), Times.Once);
    }

    [Fact]
    public void Change_ToUnknownId_LeavesActiveAppellationUnchanged()
    {
        const uint unknownId = 99999u;
        var owner = new CapturingCharacterMock();
        owner.Buffs = new Mock<IBuffs>().Object;
        var appellations = new CharacterAppellations(owner);
        // unknownId is NOT added to Appellations — should be ignored

        appellations.Change(unknownId, 0);

        Assert.NotNull(owner.LastBroadcastedPacket);
        var stream = new PacketStream();
        owner.LastBroadcastedPacket.Write(stream);
        stream.Rollback();
        stream.ReadBc();
        Assert.Equal(0u, stream.ReadUInt32()); // ActiveAppellation unchanged — still 0
    }
}
