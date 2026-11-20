using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Scripts.Commands;
using AAEmu.Game.Utils.Scripts;

using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Utils.Scripts;

[CollectionDefinition(nameof(ChangeLevelTests), DisableParallelization = true)]
public class ChangeLevelTestCollection;

[Collection(nameof(ChangeLevelTests))]
public sealed class ChangeLevelTests : IDisposable
{
    private readonly FieldInfo _levelsField = typeof(ExperienceManager)
        .GetField("_levels", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly FieldInfo _heirLevelsField = typeof(ExperienceManager)
        .GetField("_heirLevels", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly FieldInfo _housesField = typeof(HousingManager)
        .GetField("_houses", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly object _previousLevels;
    private readonly object _previousHeirLevels;
    private readonly object _previousHouses;
    private readonly WorldConfig _previousWorld;

    public ChangeLevelTests()
    {
        var experience = ExperienceManager.Instance;
        _previousLevels = _levelsField.GetValue(experience);
        _previousHeirLevels = _heirLevelsField.GetValue(experience);
        _previousHouses = _housesField.GetValue(HousingManager.Instance);
        _previousWorld = AppConfiguration.Instance.World;
        AppConfiguration.Instance.World = new WorldConfig { ExpRate = 4 };
        _housesField.SetValue(HousingManager.Instance, new Dictionary<uint, House>());
        _levelsField.SetValue(experience, new Dictionary<byte, ExperienceLevelTemplate>
        {
            [1] = new(),
            [2] = new() { TotalExp = 400, TotalMateExp = 50 },
            [3] = new() { TotalExp = 900, TotalMateExp = 110 },
            [4] = new() { TotalExp = 1800, TotalMateExp = 210 }
        });
        _heirLevelsField.SetValue(experience, new Dictionary<byte, ExperienceHeirLevelTemplate>
        {
            [0] = new() { ReqTotalExp = 3777158 },
            [1] = new() { ReqTotalExp = 5309104 }
        });
    }

    [Theory]
    [InlineData(3, 900, "2", 400, 0, 0u)]
    [InlineData(1, 0, "2", 400, 0, 0u)]
    [InlineData(3, 900, "1", 0, 0, 0u)]
    [InlineData(3, 900, "2 1", 400, 1, 3777158u)]
    public void SetsCharacterAndAllSpecializationsToExactExperience(
        byte initialLevel, int initialExperience, string arguments, int experience, byte heirLevel, uint heirExperience)
    {
        var character = new TestCharacter
        {
            Level = initialLevel,
            Experience = initialExperience,
            Ability1 = AbilityType.Fight,
            Hp = initialLevel == 1 ? 10 : 200,
            Mp = initialLevel == 1 ? 10 : 200
        };
        character.Abilities = new CharacterAbilities(character);
        character.Abilities.Abilities[AbilityType.Fight].Exp = initialExperience;
        character.Abilities.Abilities[AbilityType.Magic].Exp = 70;

        var args = arguments.Split(' ');
        new ChangeLevel().Execute(character, args, Mock.Of<IMessageOutput>());

        Assert.Equal(byte.Parse(args[0]), character.Level);
        Assert.Equal(experience, character.Experience);
        Assert.Equal(experience, character.Abilities.Abilities[AbilityType.Fight].Exp);
        Assert.Equal(experience, character.Abilities.Abilities[AbilityType.Magic].Exp);
        Assert.Equal(heirLevel, character.HeirLevel);
        Assert.Equal(heirExperience, character.HeirExp);
        Assert.Equal(100, character.Hp);
        Assert.Equal(100, character.Mp);
    }

    [Fact]
    public void ResetsHeirProgressWhenOmitted()
    {
        var character = new TestCharacter { Level = 2, Experience = 650, HeirLevel = 1, HeirExp = 4000000 };

        new ChangeLevel().Execute(character, ["2"], Mock.Of<IMessageOutput>());

        Assert.Equal(400, character.Experience);
        Assert.Equal(0, character.HeirLevel);
        Assert.Equal(0u, character.HeirExp);
    }

    [Fact]
    public void RejectsInvalidHeirLevelBeforeChangingCharacter()
    {
        var character = new TestCharacter { Level = 3, Experience = 900 };

        new ChangeLevel().Execute(character, ["2", "35"], Mock.Of<IMessageOutput>());

        Assert.Equal(3, character.Level);
        Assert.Equal(900, character.Experience);
    }

    [Fact]
    public void LowersSelectedMateAndUpdatesItsPersistentState()
    {
        var mate = new Mate { Level = 3, Experience = 110, DbInfo = new MateDb { Level = 3, Xp = 110 } };
        var character = new TestCharacter { CurrentTarget = mate };

        new ChangeLevel().Execute(character, ["1"], Mock.Of<IMessageOutput>());

        Assert.Equal(1, mate.Level);
        Assert.Equal(0, mate.Experience);
        Assert.Equal(1, mate.DbInfo.Level);
        Assert.Equal(0, mate.DbInfo.Xp);
    }

    [Fact]
    public void NamedCharacterTakesPriorityOverSelectedMate()
    {
        var target = new TestCharacter { ObjId = 0xFFFFE, Name = "ChangeLevelTestTarget", Level = 3, Experience = 900 };
        var mate = new Mate { Level = 3, Experience = 110 };
        var character = new TestCharacter { CurrentTarget = mate };
        var world = WorldManager.Instance;
        world.AddObject(target);

        try
        {
            var command = new ChangeLevel();
            var output = Mock.Of<IMessageOutput>();

            command.Execute(character, [target.Name], output);
            Assert.Equal(3, target.Level);

            command.Execute(character, [target.Name, "2"], output);
            Assert.Equal(2, target.Level);
            Assert.Equal(400, target.Experience);
            Assert.Equal(3, mate.Level);
        }
        finally
        {
            world.RemoveObject(target);
        }
    }

    public void Dispose()
    {
        _levelsField.SetValue(ExperienceManager.Instance, _previousLevels);
        _heirLevelsField.SetValue(ExperienceManager.Instance, _previousHeirLevels);
        _housesField.SetValue(HousingManager.Instance, _previousHouses);
        AppConfiguration.Instance.World = _previousWorld;
    }

    private sealed class TestCharacter() : Character(new UnitCustomModelParams())
    {
        public override int MaxHp => 100;
        public override int MaxMp => 100;
        public override void BroadcastPacket(GamePacket packet, bool self) { }
    }
}
