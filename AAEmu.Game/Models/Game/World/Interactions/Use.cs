using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.slaves;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Models.Game.World.Interactions;

public class Use : IWorldInteraction
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Execute(BaseUnit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
        uint skillId, uint doodadId, DoodadFuncTemplate objectFunc = null)
    {
        Logger.Debug($"World interaction SkillID: {skillId}");
        if (skillId == SkillsEnum.RideLongboard)
        {
            if (caster is Character rider && target is Slave board)
                SlaveManager.Instance.BindLongboard(rider, board);
            return;
        }

        if (target is Doodad doodad)
        {
            doodad.Use(caster, skillId);
            return;
        }

        if (caster is Character)
        {
            var nearby = WorldManager.GetAround<Doodad>(caster, 6f);
            if (nearby != null && nearby.Count > 0)
            {
                Doodad best = null;
                var bestDistSq = float.MaxValue;
                var cp = caster.Transform.World.Position;
                foreach (var d in nearby)
                {
                    if (DoodadManager.Instance.GetFunc(d.FuncGroupId, skillId) == null)
                        continue;
                    var dp = d.Transform.World.Position;
                    var distSq = (dp.X - cp.X) * (dp.X - cp.X) + (dp.Y - cp.Y) * (dp.Y - cp.Y) + (dp.Z - cp.Z) * (dp.Z - cp.Z);
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = d;
                    }
                }

                if (best != null)
                {
                    Logger.Debug($"Use fallback: skill {skillId} -> nearby doodad objId={best.ObjId} template={best.TemplateId} phase={best.FuncGroupId}");
                    best.Use(caster, skillId);
                }
            }
        }
    }
}
