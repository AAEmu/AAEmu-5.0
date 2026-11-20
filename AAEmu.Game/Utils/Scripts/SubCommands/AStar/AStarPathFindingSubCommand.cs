using System.Collections.Generic;
using System.Drawing;

using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Utils.Scripts.SubCommands.AStar;

public class AStarPathFindingSubCommand : SubCommandBase
{
    public AStarPathFindingSubCommand()
    {
        Title = "[AStar Path Finding]";
        Description = "Start looking for a path. Returns a list of points.";
        CallPrefix = $"{CommandManager.CommandPrefix}go||find";
        AddParameter(new StringSubCommandParameter("templateId", "templateId", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        string templateId = parameters["templateId"]; // что бы был, без праметра не работает скрипт
        var currentTarget = ((Character)character).CurrentTarget;
        var npc = currentTarget as Npc;
        if (currentTarget is null || npc == null)
        {
            SendColorMessage(messageOutput, Color.Coral, $"AStar: You need to target a Npc first");
            return;
        }

        //if (PathNode.pos1.X > 0 && PathNode.pos1.Y > 0 && PathNode.pos2.X > 0 && PathNode.pos2.Y > 0)
        if (npc.Ai.PathNode.StartPointPos != Vector3.Zero && npc.Ai.PathNode.EndPointPos != Vector3.Zero)
        {
            npc.Ai.PathNode.ZoneKey = character.Transform.ZoneId;
            var path = npc.Ai.PathNode.FindPath(npc.Ai.Owner.ParentWorld, npc.Ai.PathNode.StartPointPos, npc.Ai.PathNode.EndPointPos);
            npc.Ai.PathNode.FoundPath = new Queue<Vector3>(path);

            var foundPath = npc.Ai.PathNode.FoundPath;
            character.SendDebugMessage($"AStar: points found Total: {foundPath.Count}");
            if (foundPath.Count > 0)
            {
                var i = 0;
                foreach (var point in foundPath)
                {
                    character.SendDebugMessage($"AStar: point {i} coordinates X:{point.X}, Y:{point.Y}, Z:{point.Z}");
                    i++;
                }
            }
        }
        else
        {
            SendColorMessage(messageOutput, Color.Coral, $"AStar: to find the path, you need to set the start and end points of the route!");
        }
    }
}
