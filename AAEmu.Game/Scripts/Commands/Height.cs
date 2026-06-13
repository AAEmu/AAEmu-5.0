using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Height : ICommand
{
    public string[] CommandNames { get; set; } = new[] { "height" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target)";
    }

    public string GetCommandHelpText()
    {
        return "Gets your or target's current height and that of the supposed floor (using heightmap data)";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var targetPlayer = character;
        if (args.Length > 0)
        {
            targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstarg);
        }

        var pos = targetPlayer.Transform.World.Position;
        var zoneId = targetPlayer.Transform.ZoneId;

        // Use the new GetHeight with referenceZ for better accuracy
        var height = WorldManager.Instance.GetHeight(zoneId, pos.X, pos.Y, pos.Z);
        CommandManager.SendNormalText(this, messageOutput,
            $"{targetPlayer.Name} Z-Pos: {pos.Z:F2} - Floor: {height:F2}");
    }
}
