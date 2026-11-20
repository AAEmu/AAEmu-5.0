using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ChangeLevel : ICommand
{
    public string[] CommandNames { get; set; } = ["level", "setlevel", "set_level", "change_level", "changelevel"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <level> [heir-level]";
    }

    public string GetCommandHelpText()
    {
        return $"Sets the exact level and starting experience (1-{ExperienceManager.MaxPlayerLevel} for characters, " +
               $"1-{ExperienceManager.MaxMateLevel} for pets).\n" +
               $"Heir level is 0-{ExperienceManager.MaxPlayerHeirLevel} and defaults to 0 for characters.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var target = WorldManager.GetTargetOrSelf(character, args[0], out var firstArg);
        var argumentCount = args.Length - firstArg;
        if (argumentCount is < 1 or > 2)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var mate = firstArg == 0 ? character.CurrentTarget as Mate : null;
        var maxLevel = mate == null ? ExperienceManager.MaxPlayerLevel : ExperienceManager.MaxMateLevel;
        var experienceManager = ExperienceManager.Instance;
        if (!byte.TryParse(args[firstArg], out var level) || level < 1 || level > maxLevel)
        {
            CommandManager.SendErrorText(this, messageOutput, $"Allowed level range: 1-{maxLevel}.");
            return;
        }

        if (!experienceManager.TryGetExpForLevel(level, mate != null, out var experience))
        {
            CommandManager.SendErrorText(this, messageOutput, $"No experience threshold is configured for level {level}.");
            return;
        }

        if (mate != null)
        {
            if (argumentCount != 1)
            {
                CommandManager.SendErrorText(this, messageOutput, "Heir levels can only be set for characters.");
                return;
            }

            mate.SetLevelAndExperience(level, experience);
            return;
        }

        byte heirLevel = 0;
        if (argumentCount == 2 && (!byte.TryParse(args[firstArg + 1], out heirLevel) ||
                                  heirLevel > ExperienceManager.MaxPlayerHeirLevel))
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Allowed heir level range: 0-{ExperienceManager.MaxPlayerHeirLevel}.");
            return;
        }

        if (!experienceManager.TryGetStartingExpForHeirLevel(heirLevel, out var heirExperience))
        {
            CommandManager.SendErrorText(this, messageOutput, $"No experience threshold is configured for heir level {heirLevel}.");
            return;
        }

        if (target.SetLevelAndExperience(level, experience, heirLevel, heirExperience))
        {
            const string refreshMessage = "Reconnect to refresh the displayed heir level.";
            if (target != character)
                target.SendMessage($"A GM set your heir level to {heirLevel}. {refreshMessage}");

            CommandManager.SendNormalText(this, messageOutput,
                $"Set {target.Name} to level {level}, heir level {heirLevel}. {refreshMessage}");
        }
    }
}
