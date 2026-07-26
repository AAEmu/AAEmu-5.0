using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Achievement.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ItemEvolving : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemEvolving;
    private const int Offset = 13;
    private const int OffsetMax = Offset + 5;

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
        int value4, int value5, int value6, int value7)
    {
        // Get Player
        if (caster is not Character character)
        {
            Logger.Debug("Invalid caster type. Expected Character.");
            return;
        }

        Logger.Debug($"Special effects: ItemEvolving value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}, value5 {value5}, value6 {value6}, value7 {value7}");

        var itemId = ((SkillCastItemTarget)targetObj).Id;
        var item = ItemManager.Instance.GetItemByItemId(itemId);
        if (!(item is EquipItem && item.Template is EquipItemTemplate equipItem))
        {
            Logger.Debug($"Attempting to upgrade a non-equipment item. Item={item.Id}");
            return;
        }

        var itemId1 = ((SkillObjectItemEvolvingSupport)skillObject).M1ItemId;
        var item1 = ItemManager.Instance.GetItemByItemId(itemId1);
        if (!(item1 is EquipItem && item1.Template is EquipItemTemplate equipItem1))
        {
            Logger.Debug($"Attempting to use an item that is not equipment. Item={item1.Id}");
            return;
        }

        var itemId2 = ((SkillObjectItemEvolvingSupport)skillObject).M2ItemId;
        var item2 = ItemManager.Instance.GetItemByItemId(itemId2);
        if (!(item2 is EquipItem && item2.Template is EquipItemTemplate equipItem2))
        {
            Logger.Debug($"Attempting to use an item that is not equipment. Item={item2.Id}");
            return;
        }

        // We do not allow the item being upgraded to be destroyed
        if (item.Id == item1.Id || item.Id == item2.Id)
        {
            Logger.Warn($"You cannot use the same item for improvement! Item={item.Id}, Item1={item1.Id}, Item2={item2.Id}");
            character.SendDebugMessage($"You cannot use the same item for improvement! Item @ITEM_NAME({item.TemplateId})'s");
            return;
        }

        var autoUseAAPoint = ((SkillObjectItemEvolvingSupport)skillObject).AutoUseAAPoint;

        var beforeItemGrade = item.Grade;
        var beforeExperience = item.GemIds[3];
        Logger.Debug($"ItemEvolving: beforeItemGrade={beforeItemGrade}, beforeExperience={beforeExperience}");

        var addExperience = ItemGameData.CalculateAddExperience(equipItem1, item1, equipItem2, item2);
        var paidExperience = addExperience;
        Logger.Debug($"ItemEvolving: addExperience={addExperience}");

        // The client calculates synthesis cost as:
        // floor(target category gold multiplier * material experience / 1000).
        // Bonus experience is not paid for, so calculate the cost before applying the bonus roll.
        var goldMul = ItemGameData.Instance.GetGoldMul(equipItem.ItemRndAttrCategoryId, beforeItemGrade);
        var moneyLong = (long)goldMul * paidExperience / 1000L;
        if (goldMul < 0 || moneyLong < 0 || moneyLong > int.MaxValue)
        {
            Logger.Error($"ItemEvolving: invalid synthesis cost category={equipItem.ItemRndAttrCategoryId}, grade={beforeItemGrade}, goldMul={goldMul}, addExperience={paidExperience}, cost={moneyLong}");
            return;
        }

        var money = (int)moneyLong;
        if (character.Money < money)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            return;
        }

        var currentExperience = (int)item.GemIds[3]; //RemainingExperience; // взять текущий опыт
        Logger.Debug($"ItemEvolving: currentExperience={currentExperience}");
        currentExperience += addExperience;
        Logger.Debug($"ItemEvolving: currentExperience + addExperience={currentExperience}");

        var bonusExp = ItemGameData.Instance.GetBonus(equipItem.ItemRndAttrCategoryId, item.Grade);
        if (bonusExp > 0)
        {
            currentExperience += bonusExp;
            Logger.Debug($"ItemEvolving: currentExperience + bonusExp={currentExperience}");
            addExperience += bonusExp;
            Logger.Debug($"ItemEvolving: addExperience + bonusExp={addExperience}");
        }

        // Do not allow synthesis to cross the category's maximum evolving grade.
        // Some categories contain sentinel rows above this limit; those rows are not valid grades.
        var category = ItemGameData.Instance.GetItemRndAttrCategory(equipItem.ItemRndAttrCategoryId);
        if (category == null)
        {
            Logger.Error($"ItemEvolving: missing item rnd attr category {equipItem.ItemRndAttrCategoryId}");
            return;
        }

        var maxEvolvingGrade = category.MaxEvolvingGrade;
        var (afterItemGrade, remainingExperience) = ItemGameData.CalculateGradeAndExp(
            currentExperience,
            equipItem.ItemRndAttrCategoryId,
            beforeItemGrade,
            maxEvolvingGrade);
        item.GemIds[3] = (uint)remainingExperience; // сохраним оставшийся опыт
        Logger.Debug($"ItemEvolving: remainingExperience={remainingExperience}");
        item.Grade = afterItemGrade;
        Logger.Debug($"ItemEvolving: afterItemGrade={afterItemGrade}");

        var changeIndex = ((SkillObjectItemEvolvingSupport)skillObject).ChangeIndex; // -1 - не менять аттрибуты, (0..n) - изменить существующий атрибут
        var needChangeAttribute = changeIndex != -1;

        var beforeAttribute = new ItemEvolvingAttribute();
        var afterAttribute = new ItemEvolvingAttribute();
        var addAttributes = new List<ItemEvolvingAttribute>();
        var currentAttributes = ItemGameData.GetCurrentAttributes(item);

        if (needChangeAttribute)
        {
            if (afterItemGrade > beforeItemGrade)
            {
                var oldModifierGroupId = (int)item.GemIds[changeIndex + Offset];
                var selectAttribute = ItemGameData.Instance.GetUnitAttributeFromModifierGroup(oldModifierGroupId);
                beforeAttribute.Attribute = (ushort)selectAttribute;
                beforeAttribute.AttributeType = 0;
                beforeAttribute.AttributeValue = ItemGameData.Instance.GetUnitModifierValue(
                    oldModifierGroupId,
                    beforeItemGrade,
                    equipItem.ItemRndAttrCategoryId,
                    beforeExperience);

                var newAttribute = ItemGameData.Instance.ReplaceSelectModifierGroup(
                    equipItem.ItemRndAttrCategoryId,
                    item.Grade,
                    currentAttributes,
                    oldModifierGroupId);
                item.GemIds[changeIndex + Offset] = (uint)newAttribute.id;
                afterAttribute.Attribute = (ushort)newAttribute.attribute;
                afterAttribute.AttributeType = 0;
                afterAttribute.AttributeValue = ItemGameData.Instance.GetUnitModifierValue(
                    newAttribute.id,
                    afterItemGrade,
                    equipItem.ItemRndAttrCategoryId,
                    (uint)remainingExperience);

                Logger.Debug(
                    $"ItemEvolving: changedAttribute slot={changeIndex}, " +
                    $"beforeGroup={oldModifierGroupId}, beforeGrade={beforeItemGrade}, beforeExp={beforeExperience}, " +
                    $"beforeAttribute={selectAttribute}, beforeValue={beforeAttribute.AttributeValue}, " +
                    $"afterGroup={newAttribute.id}, afterGrade={afterItemGrade}, afterExp={remainingExperience}, " +
                    $"afterAttribute={newAttribute.attribute}, afterValue={afterAttribute.AttributeValue}");
            }
        }
        else if (ItemGameData.Instance.GetMaxUnitModifierNum(equipItem.ItemRndAttrCategoryId, item.Grade) > currentAttributes.Count && afterItemGrade > beforeItemGrade)
        {
            var randomAttributes = ItemGameData.Instance.GetRandomAttributes(equipItem.ItemRndAttrCategoryId, item.Grade, currentAttributes);

            for (var index = currentAttributes.Count; index < randomAttributes.Count + currentAttributes.Count; index++)
            {
                var newAttribute = new ItemEvolvingAttribute();
                newAttribute.Attribute = (ushort)randomAttributes[index - currentAttributes.Count].attribute;
                newAttribute.AttributeType = 0;
                newAttribute.AttributeValue = ItemGameData.Instance.GetUnitModifierValue(
                    randomAttributes[index - currentAttributes.Count].id,
                    afterItemGrade,
                    equipItem.ItemRndAttrCategoryId,
                    (uint)remainingExperience);

                addAttributes.Add(newAttribute);
                item.GemIds[index + Offset] = (uint)randomAttributes[index - currentAttributes.Count].id;
                Logger.Debug(
                    $"ItemEvolving: addAttribute id={randomAttributes[index - currentAttributes.Count].id}, " +
                    $"grade={afterItemGrade}, exp={remainingExperience}, " +
                    $"attribute={randomAttributes[index - currentAttributes.Count].attribute}, value={newAttribute.AttributeValue}");
            }
        }

        // we'll use up the material
        character.Inventory.Bag.ConsumeItem(ItemTaskType.Evolving, item1.TemplateId, item1.Count, item1);
        character.Inventory.Bag.ConsumeItem(ItemTaskType.Evolving, item2.TemplateId, item2.Count, item2);

        character.ChangeMoney(SlotType.Bag, -money);
        Logger.Debug($"ItemEvolving: cost={money}, goldMul={goldMul}, paidExperience={paidExperience}");

        character.SendPacket(new SCItemTaskSuccessPacket(
            ItemTaskType.Evolving,
            [
                new ItemGradeChange(item),
                new ItemUpdate(item)
            ],
            [])
        );

        Logger.Debug($"ItemEvolving: isEvolving={true}, itemId={itemId}, changeAttr={needChangeAttribute}, afterItemGrade={afterItemGrade}, addExperience={addExperience}, bonusExp={bonusExp}, beforeItemGrade={beforeItemGrade}");
        character.SendPacket(new SCItemEvolvingPacket(
                true,                      // bool isEvolving,
                itemId,                    // ulong itemId,
                needChangeAttribute,       //bool changeAttr,
                afterItemGrade,            // byte afterItemGrade,
                beforeAttribute,           // ItemEvolvingAttribute beforeAttribute,
                afterAttribute,            // ItemEvolvingAttribute afterAttribute,
                addAttributes,             // List<ItemEvolvingAttribute> addAttributes,
                (byte)addAttributes.Count, // addAttrCount,
                addExperience,
                bonusExp,
                beforeItemGrade
            )
        );

        character.Achievements?.TrackHiramGrade(item.TemplateId, item.Grade);

        // Log the action
        Logger.Debug($"MagicalEnchant executed by {character.Name} on item {item.Id} with skill item {item.TemplateId}");
    }
}
