using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

using Xunit;

namespace AAEmu.UnitTests.Game.GameData;

public class CharacterDefaultSkillDataTests
{
    [Fact]
    public void StartingActionSlots_UseDefaultSkillsForTheCharacterTemplate()
    {
        PlotManager.Instance.Load();
        var manager = new SkillManager();
        manager.Load();

        (Race Race, uint First, uint Second)[] expected =
        [
            (Race.Nuian, 35420, 35418),
            (Race.Dwarf, 35427, 0),
            (Race.Elf, 35421, 35422),
            (Race.Hariharan, 35423, 35424),
            (Race.Ferre, 35425, 35426),
            (Race.Warborn, 35428, 0)
        ];

        foreach (var (race, first, second) in expected)
        {
            foreach (var gender in new[] { Gender.Male, Gender.Female })
            {
                var slots = new Dictionary<byte, uint>();
                foreach (var skill in manager.GetDefaultSkills(race, gender))
                {
                    if (skill.AddToSlot)
                        Assert.True(slots.TryAdd(skill.Slot, skill.Template.Id), $"Duplicate slot {skill.Slot} for {race} {gender}");
                }

                Assert.Equal(39036u, slots[10]);
                Assert.Equal(first, slots[17]);
                Assert.Equal(second, slots.GetValueOrDefault((byte)18));
            }
        }
    }
}
