using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Resolvers;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.slaves;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.UnitTests.Utils.Mocks;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.World;

[CollectionDefinition(nameof(LongboardInteractionTests), DisableParallelization = true)]
public class LongboardInteractionTestCollection;

[Collection(nameof(LongboardInteractionTests))]
public class LongboardInteractionTests
{
    [Theory]
    [InlineData(150u)]
    [InlineData(473u)]
    public void BoardingSkillBindsDriverAndRoutesMountedSkillToBoard(uint templateId)
    {
        var rider = new Rider();
        using var board = new Vehicle(templateId, 0xFD01);

        Board(rider, board);

        Assert.Equal(AttachPointKind.Driver, rider.AttachedPoint);
        Assert.Same(rider, board.AttachedCharacters[AttachPointKind.Driver]);
        Assert.Same(board.Transform, rider.Transform.Parent);
        Assert.Equal(Vector3.Zero, rider.Transform.Local.Position);
        Assert.Collection(rider.Packets,
            packet => Assert.IsType<SCUnitAttachedPacket>(packet),
            packet =>
            {
                var binding = Assert.IsType<SCSlaveBoundPacket>(packet).Write(new PacketStream());
                binding.Pos = 0;
                Assert.Equal(rider.Id, binding.ReadUInt32());
                Assert.Equal(board.ObjId, binding.ReadBc());
            });

        var push = new SkillTemplate
        {
            SourceMount = true,
            TargetType = SkillTargetType.Self,
            TargetSelection = SkillTargetSelection.Source
        };
        SkillCaster caster = new SkillCasterMount(rider.ObjId);
        SkillCastTarget target = new SkillCastUnitTarget(rider.ObjId);

        Assert.Same(board, SkillContextResolver.AuthoritativeResolve(rider, push, ref caster, ref target));
        Assert.Equal(board.ObjId, caster.ObjId);
        Assert.Equal(board.ObjId, target.ObjId);
    }

    [Fact]
    public void ConcurrentBoardingAssignsOnlyOneDriver()
    {
        var first = new Rider();
        var second = new Rider { ObjId = 0xFD04 };
        using var board = new Vehicle(150, 0xFD01);

        Parallel.Invoke(() => Board(first, board), () => Board(second, board));

        var driver = board.AttachedCharacters[AttachPointKind.Driver];
        var other = driver == first ? second : first;
        Assert.Equal(AttachPointKind.Driver, driver.AttachedPoint);
        Assert.Same(board.Transform, driver.Transform.Parent);
        Assert.Equal(AttachPointKind.None, other.AttachedPoint);
        Assert.Null(other.Transform.Parent);
    }

    [Fact]
    public void BoardingSkillDoesNotReplaceTheCurrentDriver()
    {
        var rider = new Rider();
        var otherRider = new Rider { ObjId = 0xFD04 };
        using var board = new Vehicle(150, 0xFD01);

        Board(rider, board);
        Board(otherRider, board);

        Assert.Same(rider, board.AttachedCharacters[AttachPointKind.Driver]);
        Assert.Equal(AttachPointKind.None, otherRider.AttachedPoint);
        Assert.Empty(otherRider.Packets);
    }

    [Fact]
    public void BoardingSkillDoesNotBindAnUnrelatedVehicle()
    {
        var rider = new Rider();
        using var vehicle = new Vehicle(1, 0xFD01);

        Board(rider, vehicle);

        Assert.Equal(AttachPointKind.None, rider.AttachedPoint);
        Assert.Empty(vehicle.AttachedCharacters);
        Assert.Empty(rider.Packets);
    }

    [Fact]
    public void DismountClearsTheDriverSeatForTheNextRider()
    {
        var rider = new Rider();
        var nextRider = new Rider { ObjId = 0xFD04 };
        using var board = new Vehicle(150, 0xFD01);

        Board(rider, board);
        SlaveManager.Instance.UnbindSlave(rider, board.TlId, AttachUnitReason.SlaveUnbinding);

        Assert.Equal(AttachPointKind.None, rider.AttachedPoint);
        Assert.Null(rider.Transform.Parent);
        Assert.Empty(board.AttachedCharacters);

        Board(nextRider, board);

        Assert.Same(nextRider, board.AttachedCharacters[AttachPointKind.Driver]);
    }

    [Fact]
    public void LateVehicleMovementAfterDismountDoesNotReattachRider()
    {
        var rider = new Rider();
        using var board = new Vehicle(150, 0xFD01);
        // Parented vehicle moves use local coordinates and need no loaded terrain.
        using var vehicleParent = new Transform(null);
        board.Transform.Parent = vehicleParent;
        board.Transform.Local.SetHeight(-100);
        Board(rider, board);
        board.Transform.ResetFinalizeTransform();

        var connection = new GameConnection(null) { ActiveChar = rider };
        var packet = new CSMoveUnitPacket { Connection = connection };
        var move = new VehicleMoveType { X = 101, Y = 200, Z = -100 };
        var stream = new PacketStream().WriteBc(board.ObjId).Write((byte)MoveTypeEnum.Vehicle).Write(move).Write((byte)0);
        stream.Pos = 0;

        packet.Read(stream);
        packet.Execute();
        Assert.Equal(1, board.MovementPackets);

        SlaveManager.Instance.UnbindSlave(rider, board.TlId, AttachUnitReason.SlaveUnbinding);
        Assert.Null(rider.Transform.Parent);

        packet.Execute();

        Assert.Null(rider.Transform.Parent);
        Assert.Equal(AttachPointKind.None, rider.AttachedPoint);
        Assert.Empty(board.AttachedCharacters);
        Assert.Equal(1, board.MovementPackets);
    }

    private static void Board(Rider rider, Slave board)
    {
        var skill = new Skill(new SkillTemplate { Id = 27497 });
        var effect = new InteractionEffect { WorldInteraction = WorldInteractionType.Use };
        effect.Apply(rider, new SkillCasterUnit(rider.ObjId), board, new SkillCastUnitTarget(board.ObjId),
            new CastSkill(skill.Id, 0), new EffectSource(skill), null, DateTime.UtcNow);
    }

    private sealed class Rider : CharacterMock
    {
        public List<GamePacket> Packets { get; } = [];

        public Rider()
        {
            Id = 401;
            ObjId = 0xFD03;
            Hp = 100;
            Transform.Local.SetPosition(100, 200, 10);
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            if (self)
                Packets.Add(packet);
        }
    }

    private sealed class Vehicle : Slave, IDisposable
    {
        public int MovementPackets { get; private set; }

        public Vehicle(uint templateId, uint objId)
        {
            ObjId = objId;
            TlId = 0xFB01;
            TemplateId = templateId;
            Template = new SlaveTemplate { Id = templateId, Mountable = true };
            Hp = 100;
            Transform.Local.SetPosition(101, 200, 10);
            WorldManager.Instance.AddObject(this);
        }

        public void Dispose()
        {
            WorldManager.Instance.RemoveObject(this);
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            if (packet is SCOneUnitMovementPacket)
                MovementPackets++;
        }
    }
}
