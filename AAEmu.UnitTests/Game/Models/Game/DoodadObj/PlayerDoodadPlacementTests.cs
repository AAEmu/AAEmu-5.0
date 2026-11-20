using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.Tasks.Doodads;

using Xunit;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

[CollectionDefinition(nameof(PlayerDoodadPlacementTests), DisableParallelization = true)]
public class PlayerDoodadPlacementCollection;

[Collection(nameof(PlayerDoodadPlacementTests))]
public sealed class PlayerDoodadPlacementTests : IDisposable
{
    private const uint RiceDoodadId = 2246;
    private const uint PineDoodadId = 398;
    private const uint LadderDoodadId = 3596;
    private const uint PotatoDoodadId = 2259;
    private const uint PotatoStartPhase = 4379;
    private const float PlantX = 10;
    private const float PlantY = 20;

    private readonly Stack<Action> _restore = new();
    private readonly Dictionary<uint, House> _houses = new();
    private readonly Dictionary<ulong, ItemContainer> _containers = new();
    private Doodad _placedDoodad;

    public PlayerDoodadPlacementTests()
    {
        SetField(typeof(HousingManager), HousingManager.Instance, "_houses", _houses);
        SetField(typeof(ItemManager), ItemManager.Instance, "_allPersistentContainers", _containers);
        SetField(typeof(ItemManager), ItemManager.Instance, "_itemDoodadTemplates",
            new Dictionary<uint, ItemDoodadTemplate>
            {
                [RiceDoodadId] = new() { DoodadId = RiceDoodadId, ItemIds = [15646] },
                [PineDoodadId] = new() { DoodadId = PineDoodadId, ItemIds = [14898] },
                [LadderDoodadId] = new() { DoodadId = LadderDoodadId, ItemIds = [19129] },
                [PotatoDoodadId] = new() { DoodadId = PotatoDoodadId, ItemIds = [15659] }
            });
        SetField(typeof(DoodadManager), DoodadManager.Instance, "_templates", new Dictionary<uint, DoodadTemplate>
        {
            [RiceDoodadId] = new() { Id = RiceDoodadId },
            [PineDoodadId] = new() { Id = PineDoodadId },
            [LadderDoodadId] = new() { Id = LadderDoodadId },
            [PotatoDoodadId] = new()
            {
                Id = PotatoDoodadId,
                ClimateId = Climate.Any,
                TotalDoodadGrowthTime = 600_000,
                FuncGroups = [new() { Id = PotatoStartPhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Start }]
            }
        });
        SetField(typeof(WorldManager), WorldManager.Instance, "_worlds", new Dictionary<uint, InstanceWorld>
        {
            [0] = new()
        });

        var previousWorld = AppConfiguration.Instance.World;
        _restore.Push(() => AppConfiguration.Instance.World = previousWorld);
        AppConfiguration.Instance.World = new WorldConfig();

        // Keep experience awards and external persistence out of the placement fixture.
        var formula = new Formula();
        typeof(Formula).GetProperty("Expression", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(formula, (Func<Dictionary<string, double>, double>)(_ => 0));
        SetField(typeof(FormulaManager), FormulaManager.Instance, "_formulas", new Dictionary<uint, Formula>
        {
            [(uint)FormulaKind.ExpByLaborPower] = formula
        });
        SetField(typeof(MySQL), null, "s_connectionString", "Port=invalid");
    }

    [Theory]
    [InlineData(RiceDoodadId, 15646u, 25536u, 5, 0)]
    [InlineData(PineDoodadId, 14898u, 10953u, 13, 8)]
    public void UnprotectedPlanting_SpendsFiveLaborAndOneSeed(
        uint doodadId, uint itemId, uint useSkillId, short initialLabor, short expectedLabor)
    {
        var (character, item) = CreateCharacterWithItem(itemId, useSkillId, initialLabor);

        PlaceDoodad(character, doodadId, item);

        AssertPlacement(character, item, expectedLabor);
    }

    [Theory]
    [InlineData(RiceDoodadId, 15646u, 25536u)]
    [InlineData(PineDoodadId, 14898u, 10953u)]
    public void UnprotectedPlanting_WithInsufficientLabor_KeepsSeedInBag(uint doodadId, uint itemId, uint useSkillId)
    {
        var (character, item) = CreateCharacterWithItem(itemId, useSkillId, 4);

        PlaceDoodad(character, doodadId, item);

        Assert.Null(_placedDoodad);
        Assert.Equal(4, character.LaborPower);
        Assert.Equal(2, item.Count);
        Assert.Same(item, character.Inventory.Bag.GetItemByItemId(item.Id));
    }

    [Theory]
    [InlineData(false, FarmType.Farm)]
    [InlineData(true, FarmType.Invalid)]
    public void ProtectedPlanting_AllowsPlacementWithZeroLabor(bool onHousing, FarmType farmType)
    {
        var (character, item) = CreateCharacterWithItem(15646, 25536, 0);
        if (onHousing)
        {
            var house = new House { Id = 1, OwnerId = character.Id, Template = new HousingTemplate { GardenRadius = 8 } };
            house.Transform.Local.SetPosition(PlantX, PlantY, 0);
            _houses.Add(house.Id, house);
        }

        PlaceDoodad(character, RiceDoodadId, item, farmType);

        AssertPlacement(character, item, 0);
    }

    [Fact]
    public async Task PotatoPlacement_InitializesGrowthBeforePublishingDoodad()
    {
        var (character, item) = CreateCharacterWithItem(15659, 25536, 5);
        var growth = new ObservedGrowth { Delay = 30_000, StartScale = 500, EndScale = 750, NextPhase = 4456 };
        SetField(typeof(DoodadManager), DoodadManager.Instance, "_funcsByGroups", new Dictionary<uint, List<DoodadFunc>>());
        SetField(typeof(DoodadManager), DoodadManager.Instance, "_phaseFuncs", new Dictionary<uint, List<DoodadPhaseFunc>>
        {
            [PotatoStartPhase] = [new() { GroupId = PotatoStartPhase, FuncId = 583, FuncType = nameof(DoodadFuncGrowth) }]
        });
        SetField(typeof(DoodadManager), DoodadManager.Instance, "_phaseFuncTemplates",
            new Dictionary<string, Dictionary<uint, DoodadPhaseFuncTemplate>>
            {
                [nameof(DoodadFuncGrowth)] = new() { [583] = growth }
            });

        var placementThread = Environment.CurrentManagedThreadId;
        PlaceDoodad(character, PotatoDoodadId, item);
        var packet = new SCDoodadCreatedPacket(_placedDoodad).Write(new PacketStream());
        var (growthThread, wasVisible) = await growth.Applied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(wasVisible);
        Assert.Equal(placementThread, growthThread);
        packet.Pos = 0;
        Assert.Equal(_placedDoodad.ObjId, packet.ReadBc());
        Assert.Equal((long)PotatoStartPhase, packet.ReadPisc(4)[1]);
        packet.ReadByte(); // Flags
        packet.ReadBc(); // Owner
        packet.ReadBc(); // Parent
        packet.ReadByte(); // Attach point
        packet.ReadPosition();
        packet.Pos += 3 * sizeof(short); // Rotation
        Assert.Equal(1f, packet.ReadSingle());
        packet.ReadUInt32(); // Owner ID
        packet.ReadUInt64(); // UCC ID
        packet.ReadUInt32(); // Item template
        Assert.InRange(packet.ReadUInt32(), 1u, 30_000u);
        Assert.IsType<DoodadFuncGrowthTask>(_placedDoodad.FuncTask);
        AssertPlacement(character, item, 0);
    }

    [Fact]
    public void ValidNonPlantPlacement_DoesNotChargePlantingLabor()
    {
        var (character, item) = CreateCharacterWithItem(19129, 24386, 0);

        PlaceDoodad(character, LadderDoodadId, item);

        AssertPlacement(character, item, 0);
    }

    [Fact]
    public void UnrelatedSourceItem_CannotBeUsedToPlant()
    {
        var (character, item) = CreateCharacterWithItem(19129, 24386, 5);

        PlaceDoodad(character, RiceDoodadId, item);

        Assert.Null(_placedDoodad);
        Assert.Equal(5, character.LaborPower);
        Assert.Equal(2, item.Count);
    }

    private static (Character Character, Item Item) CreateCharacterWithItem(uint itemId, uint skillId, short labor)
    {
        var character = new Character(null) { Id = 1, NumInventorySlots = 10 };
        character.Transform.WorldId = 0;
        character.InitializeLaborCache(labor, DateTime.UtcNow);
        character.Inventory = new Inventory(character);
        var bag = character.Inventory.Bag;
        var item = new Item(1, new ItemTemplate { Id = itemId, UseSkillId = skillId, MaxCount = 100 }, 2)
        {
            HoldingContainer = bag,
            Slot = 0,
            SlotType = SlotType.Bag
        };
        bag.Items.Add(item);
        return (character, item);
    }

    private void PlaceDoodad(Character character, uint doodadId, Item item, FarmType farmType = FarmType.Invalid)
    {
        _placedDoodad = DoodadManager.CreatePlayerDoodad(character, doodadId, PlantX, PlantY, 0, 0, 1, item.Id, farmType);
    }

    private void AssertPlacement(Character character, Item item, short labor)
    {
        Assert.NotNull(_placedDoodad);
        Assert.Same(_placedDoodad, WorldManager.Instance.GetDoodad(_placedDoodad.ObjId));
        Assert.Equal(item.TemplateId, _placedDoodad.ItemTemplateId);
        Assert.Equal(labor, character.LaborPower);
        Assert.Equal(1, item.Count);
    }

    private void SetField(Type type, object target, string name, object value)
    {
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(target);
        _restore.Push(() => field.SetValue(target, previous));
        field.SetValue(target, value);
    }

    public void Dispose()
    {
        try
        {
            if (_placedDoodad != null)
            {
                _placedDoodad.IsPersistent = false;
                _placedDoodad.Delete();
                ObjectIdManager.Instance.ReleaseId(_placedDoodad.ObjId);
                if (_placedDoodad.DbId != 0)
                {
                    var dirtyDoodads = (ConcurrentDictionary<uint, Doodad>)typeof(Doodad)
                        .GetField("_dirtyDoodads", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
                    dirtyDoodads.TryRemove(new KeyValuePair<uint, Doodad>(_placedDoodad.DbId, _placedDoodad));
                    DoodadIdManager.Instance.ReleaseId(_placedDoodad.DbId);
                }
            }

            foreach (var containerId in _containers.Keys)
                ContainerIdManager.Instance.ReleaseId((uint)containerId);
        }
        finally
        {
            while (_restore.TryPop(out var restore))
                restore();
        }
    }

    private sealed class ObservedGrowth : DoodadFuncGrowth
    {
        public TaskCompletionSource<(int Thread, bool IsVisible)> Applied { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool Use(BaseUnit caster, Doodad owner, ref Doodad.PhaseRollContext context)
        {
            var stop = base.Use(caster, owner, ref context);
            Applied.SetResult((Environment.CurrentManagedThreadId, owner.IsVisible));
            return stop;
        }
    }
}
