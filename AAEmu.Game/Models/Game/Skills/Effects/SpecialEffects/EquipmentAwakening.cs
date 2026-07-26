using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class EquipmentAwakening : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.EquipmentAwakening;

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int mappingGroupId, int value2,
        int value3, int value4, int value5, int value6, int value7)
    {
        if (caster is not Character character)
        {
            Logger.Warn("EquipmentAwakening: caster is not Character");
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn($"EquipmentAwakening: expected SkillCastItemTarget, got {targetObj?.GetType().Name ?? "null"}");
            return;
        }

        if (casterObj is not SkillItem skillItem)
        {
            Logger.Warn($"EquipmentAwakening: expected SkillItem, got {casterObj?.GetType().Name ?? "null"}");
            return;
        }

        var sourceItem = character.Inventory.Bag.GetItemByItemId(itemTarget.Id);
        if (sourceItem is not EquipItem sourceEquip || sourceEquip.Template is not EquipItemTemplate)
        {
            Logger.Warn($"EquipmentAwakening: target item {itemTarget.Id} is missing or is not equipment");
            return;
        }

        if (sourceItem.HasFlag(ItemFlag.DisabledEnchant))
        {
            Logger.Info($"EquipmentAwakening: item {sourceItem.Id} is disabled for enchanting and must be restored first");
            character.SendPacket(new SCItemChangeMappingResultPacket(sourceItem, sourceItem,
                (uint)mappingGroupId, (byte)GradeTamperingResult.Break));
            return;
        }

        var group = ItemGameData.Instance.GetItemChangeMappingGroup(mappingGroupId);
        if (group.Id == 0)
        {
            Logger.Error($"EquipmentAwakening: mapping group {mappingGroupId} was not loaded");
            return;
        }

        var targetTemplateId = ItemGameData.Instance.GetMappingItem(mappingGroupId, sourceItem.Grade, (int)sourceItem.TemplateId);
        if (targetTemplateId == 0)
        {
            Logger.Error($"EquipmentAwakening: no mapping group={mappingGroupId}, source={sourceItem.TemplateId}, grade={sourceItem.Grade}");
            return;
        }

        var bonusBefore = sourceItem.MappingFailBonus;
        var effectiveSuccessBasisPoints = GetEffectiveSuccessBasisPoints(group.Success, bonusBefore);
        var result = Roll(effectiveSuccessBasisPoints, group.Disable);
        if (result != GradeTamperingResult.Success)
        {
            if (result == GradeTamperingResult.Fail)
            {
                var failBonusPercent = Math.Max(0, group.FailBonus / 100);
                sourceItem.MappingFailBonus = (byte)Math.Min(byte.MaxValue, bonusBefore + failBonusPercent);
            }
            else if (result == GradeTamperingResult.Break)
            {
                // Client item flag 0x40 is exposed as isEnchantDisable.
                // Keep accumulated fail bonus; a restore scroll should only remove the lock.
                sourceItem.SetFlag(ItemFlag.DisabledEnchant);
            }

            // MappingFailBonus is persisted inside the equipment details blob.
            // Send the full item-detail packet as well: ItemUpdate uses the alternate
            // additional-details layout and does not contain MappingFailBonus.
            // ItemUpdate does not refresh ItemFlags in the 5.0 client.
            // SetFlagsBits updates the broken-item overlay immediately without a relog.
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.EquipmentAwakening,
                [new ItemUpdateBits(sourceItem)], []));
            character.SendPacket(new SCItemDetailUpdatedPacket(sourceItem));
            character.SendPacket(new SCItemChangeMappingResultPacket(sourceItem, sourceItem, (uint)mappingGroupId, (byte)result));

            var nextEffectiveSuccessBasisPoints = GetEffectiveSuccessBasisPoints(group.Success, sourceItem.MappingFailBonus);
            Logger.Info($"EquipmentAwakening: result={result}, item={sourceItem.Id}, " +
                        $"base={group.Success / 100.0:F2}%, bonus={bonusBefore:F2}%->{sourceItem.MappingFailBonus:F2}%, " +
                        $"effective={effectiveSuccessBasisPoints / 100.0:F2}%->{nextEffectiveSuccessBasisPoints / 100.0:F2}%");
            return;
        }

        var targetItem = ItemManager.Instance.Create((uint)targetTemplateId, sourceItem.Count, sourceItem.Grade);
        if (targetItem is not EquipItem targetEquip)
        {
            Logger.Error($"EquipmentAwakening: mapped template {targetTemplateId} is not equipment");
            return;
        }

        CopyEquipmentState(sourceEquip, targetEquip, group.EvolvingExpInherit);
        targetEquip.Grade = (byte)Math.Max(0, sourceEquip.Grade - Math.Max(0, value4));
        targetEquip.MappingFailBonus = 0;
        targetEquip.RemoveFlag(ItemFlag.DisabledEnchant);

        var sourceSlot = sourceItem.Slot;
        if (!character.Inventory.Bag.RemoveItem(ItemTaskType.Invalid, sourceItem, true))
        {
            Logger.Error($"EquipmentAwakening: failed to remove source item {sourceItem.Id}");
            return;
        }

        if (!character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, targetItem, sourceSlot))
        {
            Logger.Error($"EquipmentAwakening: failed to insert target item {targetItem.Id} into slot {sourceSlot}");
            return;
        }

        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.EquipmentAwakening,
            [new ItemRemove(sourceItem), new ItemAdd(targetItem)], []));
        character.SendPacket(new SCItemChangeMappingResultPacket(sourceItem, targetItem, (uint)mappingGroupId, (byte)GradeTamperingResult.Success));
        character.Achievements?.TrackHiramGrade(targetItem.TemplateId, targetItem.Grade);

        Logger.Info($"EquipmentAwakening: success character={character.Name}, scroll={skillItem.ItemTemplateId}, " +
                    $"source={sourceItem.TemplateId}:{sourceItem.Grade}, target={targetItem.TemplateId}:{targetItem.Grade}, " +
                    $"group={mappingGroupId}, inheritExp={group.EvolvingExpInherit}");
    }

    private static int GetEffectiveSuccessBasisPoints(int baseSuccessBasisPoints, byte failBonusPercent)
    {
        return Math.Clamp(baseSuccessBasisPoints + failBonusPercent * 100, 0, 10000);
    }

    private static GradeTamperingResult Roll(int effectiveSuccessBasisPoints, int disableBasisPoints)
    {
        var success = Math.Clamp(effectiveSuccessBasisPoints, 0, 10000);
        var disable = Math.Clamp(disableBasisPoints, 0, 10000 - success);
        var roll = Rand.Next(0, 10000);

        if (roll < success)
            return GradeTamperingResult.Success;

        if (roll < success + disable)
            return GradeTamperingResult.Break;

        return GradeTamperingResult.Fail;
    }

    private static void CopyEquipmentState(EquipItem source, EquipItem target, bool inheritEvolvingExp)
    {
        ItemGameData.CopyAllAttributes(source, target);
        if (!inheritEvolvingExp)
            target.GemIds[3] = 0;

        target.ItemFlags = source.ItemFlags;
        target.ImageItemTemplateId = source.ImageItemTemplateId;
        target.Durability = source.Durability;
        target.ChargeCount = source.ChargeCount;
        target.ChargeTime = source.ChargeTime;
        target.ChargeProcTime = source.ChargeProcTime;
        target.TemperPhysical = source.TemperPhysical;
        target.TemperMagical = source.TemperMagical;
        target.RuneId = source.RuneId;
        target.UccId = source.UccId;
        target.MadeUnitId = source.MadeUnitId;
        target.UnsecureTime = source.UnsecureTime;
        target.ExpirationTime = source.ExpirationTime;
        target.ExpirationOnlineMinutesLeft = source.ExpirationOnlineMinutesLeft;
    }
}
