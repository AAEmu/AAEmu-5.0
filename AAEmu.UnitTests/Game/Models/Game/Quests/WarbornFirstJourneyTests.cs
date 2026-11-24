using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils.DB;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

[CollectionDefinition(nameof(WarbornFirstJourneyTests), DisableParallelization = true)]
public class WarbornFirstJourneyTestCollection;

[Collection(nameof(WarbornFirstJourneyTests))]
public sealed class WarbornFirstJourneyTests : IDisposable
{
    private const uint FirstJourneyQuestId = 8228;
    private readonly WorldConfig _previousWorld = AppConfiguration.Instance.World;
    private readonly bool _previousDebugInfo = AppConfiguration.Instance.DebugInfo;

    public WarbornFirstJourneyTests()
    {
        AppConfiguration.Instance.World = new WorldConfig { FirstJourneyEnable = false, ExpRate = 0 };
        AppConfiguration.Instance.DebugInfo = false;

        using var connection = SQLite.CreateConnection(AppContext.BaseDirectory, "Data/compact.sqlite3");
        TodayAssignmentGameData.Instance.Load(connection, null);
        UnitRequirementsGameData.Instance.Load(connection, null);
        ZoneManager.Instance.Load();
        QuestManager.Instance.Load();
        ExperienceManager.Instance.Load();
        QuestIdManager.Instance.Initialize();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    public void OnPlayerJoin_WarbornInStartingZone_UnlocksFirstStoryQuest(int minutesSinceCreation)
    {
        var character = CreateCharacter(Race.Warborn, 157, minutesSinceCreation);
        var storyStart = Assert.Single(QuestManager.Instance.GetTemplate(8115).GetComponents(QuestComponentKind.Start));
        Assert.False(UnitRequirementsGameData.Instance.CanComponentRun(storyStart, character));

        WorldManager.Instance.OnPlayerJoin(character);

        Assert.True(character.Quests.TryGetActiveQuest(FirstJourneyQuestId, out var journey));
        Assert.Equal(QuestAcceptorType.Sphere, journey.QuestAcceptorType);
        Assert.Equal(2600u, journey.AcceptorId);
        Assert.Equal(QuestComponentKind.Reward, journey.Step);

        WorldManager.Instance.OnPlayerJoin(character);
        Assert.Same(journey, character.Quests.ActiveQuests[FirstJourneyQuestId]);

        QuestManager.Instance.DoQueuedEvaluations();
        Assert.True(character.Quests.HasQuestCompleted(FirstJourneyQuestId));
        Assert.True(UnitRequirementsGameData.Instance.CanComponentRun(storyStart, character));

        WorldManager.Instance.OnPlayerJoin(character);
        Assert.Empty(character.Quests.ActiveQuests);
    }

    [Theory]
    [InlineData(Race.Hariharan, 157u)]
    [InlineData(Race.Warborn, 287u)]
    public void OnPlayerJoin_OutsideWarbornStartingAreaOrOtherRace_DoesNotStartWarbornJourney(Race race, uint zoneKey)
    {
        var character = CreateCharacter(race, zoneKey, 0);

        WorldManager.Instance.OnPlayerJoin(character);

        Assert.Empty(character.Quests.ActiveQuests);
    }

    [Fact]
    public void OnZoneChange_WarbornReturnsToStartingZone_StartsFirstJourney()
    {
        var character = CreateCharacter(Race.Warborn, 287, 120);
        // Avoid friend notifications, which need the server's MySQL-backed friend list.
        typeof(Character).GetField("_isOnline", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, true);

        character.OnZoneChange(287, 157);

        Assert.True(character.Quests.HasQuest(FirstJourneyQuestId));
        QuestManager.Instance.DoQueuedEvaluations();
    }

    [Fact]
    public void OnZoneChange_WarbornStillLoading_DoesNotStartFirstJourney()
    {
        var character = CreateCharacter(Race.Warborn, 287, 0);

        character.OnZoneChange(287, 157);

        Assert.Empty(character.Quests.ActiveQuests);
    }

    private static Character CreateCharacter(Race race, uint zoneKey, int minutesSinceCreation)
    {
        var updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var character = new Character(null)
        {
            Id = 1,
            Name = "FirstJourneyTest",
            Race = race,
            Level = 1,
            Ability1 = AbilityType.Fight,
            Ability2 = AbilityType.None,
            Ability3 = AbilityType.None,
            Created = updated.AddMinutes(-minutesSinceCreation),
            Updated = updated,
            Transform = new Transform(null, null) { WorldId = WorldManager.DefaultWorldId, ZoneId = zoneKey }
        };
        character.Abilities = new CharacterAbilities(character);
        character.Quests = new CharacterQuests(character);
        return character;
    }

    public void Dispose()
    {
        AppConfiguration.Instance.World = _previousWorld;
        AppConfiguration.Instance.DebugInfo = _previousDebugInfo;
    }
}
