using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Special effect 156: removes the DisabledEnchant (0x40) item flag.
/// The skill/reagent pipeline consumes the restoration item; this action only
/// restores the target equipment and synchronizes its inventory bits.
/// </summary>
public class RestoreDisableEnchant : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.RestoreDisableEnchant;

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target,
        SkillCastTarget targetObj, CastAction castObj, Skill skill, SkillObject skillObject,
        DateTime time, int value1, int value2, int value3, int value4, int value5, int value6, int value7)
    {
        if (caster is not Character character)
        {
            Logger.Warn("RestoreDisableEnchant: caster is not Character");
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn($"RestoreDisableEnchant: expected SkillCastItemTarget, got {targetObj?.GetType().Name ?? "null"}");
            return;
        }

        var item = character.Inventory.Bag.GetItemByItemId(itemTarget.Id);
        if (item is not EquipItem equipment)
        {
            Logger.Warn($"RestoreDisableEnchant: target item {itemTarget.Id} is missing or is not equipment");
            return;
        }

        if (!equipment.HasFlag(ItemFlag.DisabledEnchant))
        {
            Logger.Info($"RestoreDisableEnchant: item {equipment.Id} is not disabled");
            return;
        }

        // SetFlagsBits (ItemUpdateBits) is sufficient for adding the broken bit,
        // but the 5.0 client does not clear an already-present bit with that action.
        // Preserve the old value and use UpdateFlags so the client can replace the
        // complete flags byte and remove the broken overlay immediately.
        var previousFlags = (byte)equipment.ItemFlags;
        equipment.RemoveFlag(ItemFlag.DisabledEnchant);

        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.RestoreDisableEnchant,
            [new ItemUpdateSecurity(
                equipment,
                (byte)equipment.ItemFlags,
                previousFlags,
                false,
                false,
                equipment.HasFlag(ItemFlag.Unpacked))], []));
        character.SendPacket(new SCItemDetailUpdatedPacket(equipment));

        Logger.Info($"RestoreDisableEnchant: restored item={equipment.Id}, template={equipment.TemplateId}, character={character.Name}");
    }
}
