using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCUnitStatePacketTests
{
    [Fact]
    public void Write_NpcTemplatePassive_IsSerializedOnlyAsAPassive()
    {
        var buffs = new Mock<IBuffs>();
        var npc = new Npc
        {
            Template = new NpcTemplate(),
            Hp = 409313,
            Buffs = buffs.Object,
            CanFly = true,
            Spawner = new NpcSpawner { Position = new WorldSpawnPosition() }
        };
        npc.Ai = new BigMonsterRoamingAiCharacter { Owner = npc };
        npc.Template.PassiveBuffs.Add(new NpcPassiveBuff
        {
            PassiveBuffId = 173,
            PassiveBuff = new PassiveBuffTemplate { Id = 173, BuffId = 3590 }
        });
        var hidden = new List<Buff>();
        buffs.Setup(x => x.GetAllBuffs(It.IsAny<List<Buff>>(), It.IsAny<List<Buff>>(), It.IsAny<List<Buff>>(), false))
            .Callback((List<Buff> good, List<Buff> bad, List<Buff> netHidden, bool includeAll) => netHidden.AddRange(hidden));

        var withoutHidden = new SCUnitStatePacket(npc).Write(new PacketStream()).GetBytes();
        hidden.Add(new Buff(npc, npc, new SkillCasterUnit(), new BuffTemplate { Id = 6613 }, null, DateTime.UtcNow));
        hidden.Add(new Buff(npc, npc, new SkillCasterUnit(), new BuffTemplate { Id = 3590 }, null, DateTime.UtcNow)
        {
            Passive = true
        });
        var actual = new SCUnitStatePacket(npc).Write(new PacketStream());
        var hiddenCountOffset = withoutHidden.Length - 1;

        Assert.True(withoutHidden.AsSpan(0, hiddenCountOffset).SequenceEqual(actual.GetBytes().AsSpan(0, hiddenCountOffset)));
        actual.Pos = hiddenCountOffset;
        Assert.Equal(1, actual.ReadByte());
        actual.ReadUInt32(); // Buff index
        actual.ReadByte(); // Caster type
        actual.ReadBc(); // Caster object ID
        actual.ReadUInt32(); // Caster ID
        actual.ReadByte(); // Caster level
        actual.ReadUInt16(); // Caster ability level
        actual.ReadPisc(4); // Timing
        Assert.Equal(6613, actual.ReadPisc(4)[0]);
    }
}
