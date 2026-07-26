using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ItemEvolvingReRoll : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemEvolvingReRoll;

    private const int AttributeOffset = 13;
    private const int AttributeSlotCount = 5;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4,
        int value5,
        int value6,
        int value7)
    {
        if (caster is not Character character)
        {
            Logger.Debug("ItemEvolvingReRoll: invalid caster type. Expected Character.");
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn($"ItemEvolvingReRoll: invalid target type {targetObj?.GetType().Name ?? "null"}.");
            return;
        }

        var item = ItemManager.Instance.GetItemByItemId(itemTarget.Id);
        if (item is not EquipItem || item.Template is not EquipItemTemplate equipTemplate)
        {
            Logger.Warn($"ItemEvolvingReRoll: target item {itemTarget.Id} is not equipment.");
            return;
        }

        // In 5.0 the selected attribute index is normally sent in SkillObjectUnk10.
        // Keep fallbacks for builds that still serialize it through the evolving object
        // or through value1 of the special effect.
        var changeIndex = skillObject switch
        {
            SkillObjectUnk10 rerollObject => rerollObject.ChangeIndex,
            SkillObjectItemEvolvingSupport evolvingObject => evolvingObject.ChangeIndex,
            _ => value1
        };

        if (changeIndex < 0 || changeIndex >= AttributeSlotCount)
        {
            Logger.Warn($"ItemEvolvingReRoll: invalid changeIndex={changeIndex}, value1={value1}, skillObject={skillObject?.GetType().Name ?? "null"}.");
            return;
        }

        var gemIndex = AttributeOffset + changeIndex;
        var oldModifierGroupId = (int)item.GemIds[gemIndex];
        if (oldModifierGroupId <= 0)
        {
            Logger.Warn($"ItemEvolvingReRoll: item={item.Id} has no attribute in slot={changeIndex} (GemIds[{gemIndex}]={oldModifierGroupId}).");
            return;
        }

        var oldUnitAttributeId = ItemGameData.Instance.GetUnitAttributeFromModifierGroup(oldModifierGroupId);
        if (oldUnitAttributeId <= 0)
        {
            Logger.Warn($"ItemEvolvingReRoll: modifierGroupId={oldModifierGroupId} has no unit attribute mapping.");
            return;
        }

        var currentAttributes = ItemGameData.GetCurrentAttributes(item);
        var replacement = ItemGameData.Instance.ReplaceSelectModifierGroup(
            equipTemplate.ItemRndAttrCategoryId,
            item.Grade,
            currentAttributes,
            oldModifierGroupId);

        if (replacement.id <= 0 || replacement.attribute <= 0)
        {
            Logger.Warn(
                $"ItemEvolvingReRoll: no replacement found for item={item.Id}, " +
                $"category={equipTemplate.ItemRndAttrCategoryId}, grade={item.Grade}, slot={changeIndex}, " +
                $"oldGroup={oldModifierGroupId}, oldAttribute={oldUnitAttributeId}, " +
                $"currentGroups=[{string.Join(",", currentAttributes)}].");

            // Always finish the client-side reroll request. Without a response the
            // replacement button remains disabled until the window is reopened.
            var unchangedAttribute = new ItemEvolvingAttribute
            {
                Attribute = (ushort)oldUnitAttributeId,
                AttributeType = 0,
                AttributeValue = ItemGameData.Instance.GetUnitModifierValue(
                    oldModifierGroupId,
                    item.Grade,
                    equipTemplate.ItemRndAttrCategoryId,
                    item.GemIds[3])
            };

            character.SendPacket(new SCItemEvolvingPacket(
                false,
                item.Id,
                false,
                item.Grade,
                unchangedAttribute,
                unchangedAttribute,
                new List<ItemEvolvingAttribute>(),
                0,
                0,
                0,
                item.Grade));
            return;
        }

        var currentExp = item.GemIds[3];
        var beforeDisplayValue = ItemGameData.Instance.GetUnitModifierValue(
            oldModifierGroupId,
            item.Grade,
            equipTemplate.ItemRndAttrCategoryId,
            currentExp);
        var afterDisplayValue = ItemGameData.Instance.GetUnitModifierValue(
            replacement.id,
            item.Grade,
            equipTemplate.ItemRndAttrCategoryId,
            currentExp);

        var beforeAttribute = new ItemEvolvingAttribute
        {
            Attribute = (ushort)oldUnitAttributeId,
            AttributeType = 0,
            AttributeValue = beforeDisplayValue
        };

        var afterAttribute = new ItemEvolvingAttribute
        {
            Attribute = (ushort)replacement.attribute,
            AttributeType = 0,
            AttributeValue = afterDisplayValue
        };

        item.GemIds[gemIndex] = (uint)replacement.id;

        // The skill/reagent pipeline already consumes the reroll stone. Here we only
        // update the equipment item and send the reroll result to the client.
        character.SendPacket(new SCItemTaskSuccessPacket(
            ItemTaskType.Evolving,
            [new ItemUpdate(item)],
            []));

        character.SendPacket(new SCItemEvolvingPacket(
            false,                              // standalone reroll, not synthesis
            item.Id,
            true,
            item.Grade,
            beforeAttribute,
            afterAttribute,
            new List<ItemEvolvingAttribute>(),
            0,
            0,
            0,
            item.Grade));

        Logger.Debug(
            $"ItemEvolvingReRoll: item={item.Id}, slot={changeIndex}, " +
            $"beforeGroup={oldModifierGroupId}, beforeAttribute={oldUnitAttributeId}, " +
            $"category={equipTemplate.ItemRndAttrCategoryId}, grade={item.Grade}, " +
            $"currentExp={currentExp}, gradeExp={ItemGameData.Instance.GetGradeExp(equipTemplate.ItemRndAttrCategoryId, item.Grade)}, " +
            $"beforeValue={beforeAttribute.AttributeValue}, " +
            $"afterGroup={replacement.id}, afterAttribute={replacement.attribute}, afterValue={afterAttribute.AttributeValue}");
    }
}
