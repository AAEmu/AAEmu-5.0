using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class RecoverExpEffect : EffectTemplate
{
    public bool NeedMoney { get; set; }
    public bool NeedLaborPower { get; set; }

    public bool NeedPriest { get; set; }
    // TODO 1.2 // public bool Penaltied { get; set; }

    public override bool OnActionTime => false;

    // The client displays the labor cost as a ratio of the recoverable exp (observed in-game:
    // 947 recoverable exp -> 21 labor, i.e. ~45 exp per labor). Charge the matching amount so the
    // server stays in sync with the figure the player confirmed.
    private const double ExpPerLabor = 45.0;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("RecoverExpEffect");

        // The "Blessing" skill (12084) is self-cast, so the praying player is the target/caster.
        var character = (target as Character) ?? (caster as Character);
        if (character == null)
            return;

        var recoverable = character.RecoverableExp;
        if (recoverable <= 0)
            return; // nothing to recover

        // Charge labor up-front; abort cleanly (no recovery) if the player can't afford it.
        if (NeedLaborPower)
        {
            var laborCost = Math.Max(1, (int)Math.Round(recoverable / ExpPerLabor));
            if (character.LaborPower < laborCost)
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
                return;
            }
            character.ChangeLabor((short)-laborCost, 0);
        }

        // TODO: NeedMoney variant (not used by the priestess Blessing; effect 5 is the penaltied/free form)

        // Restore the lost experience (reverse of Character.ApplyDeathPenalty) and clear the recoverable pool.
        character.Experience += recoverable;
        character.RecoverableExp -= recoverable;
        character.SendPacket(new SCExpChangedPacket(character.ObjId, recoverable, false));
        character.SendPacket(new SCRecoverableExpPacket(character.ObjId, character.RecoverableExp, 0, 0));
    }
}
