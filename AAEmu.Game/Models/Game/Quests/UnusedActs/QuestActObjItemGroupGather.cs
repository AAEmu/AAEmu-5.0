using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Gather a number of items where any item in the configured quest item group counts.
/// Mirrors QuestActObjItemGather but matches an item group (item_group_id) instead of a single ItemId.
/// </summary>
public class QuestActObjItemGroupGather(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint ItemGroupId { get; set; }
    public bool Cleanup { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    private int GroupItemCount(Quest quest)
    {
        var total = 0;
        foreach (var itemId in QuestManager.Instance.GetGroupItems(ItemGroupId))
            total += quest.Owner.Inventory.GetItemsCount(itemId);
        return total;
    }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest {quest.TemplateId}, ItemGroupId {ItemGroupId}, Count {currentObjectiveCount}/{Count}");
        SetObjective(quest, GroupItemCount(quest));
        return GetObjective(quest) >= Count;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        SetObjective(quest, GroupItemCount(quest));
        quest.Owner.Events.OnItemGather += questAct.OnItemGather;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        base.FinalizeAction(quest, questAct);
        quest.Owner.Events.OnItemGather -= questAct.OnItemGather;
    }

    public override void OnItemGather(QuestAct questAct, object sender, OnItemGatherArgs args)
    {
        if (questAct.Id != ActId)
            return;
        if (!QuestManager.Instance.CheckGroupItem(ItemGroupId, args.ItemId))
            return;
        SetObjective((QuestAct)questAct, GroupItemCount(questAct.QuestComponent.Parent.Parent));
    }

    public override void QuestCleanup(Quest quest)
    {
        base.QuestCleanup(quest);
        if (Cleanup)
            ConsumeGroupItems(quest);
    }

    public override void QuestDropped(Quest quest)
    {
        base.QuestDropped(quest);
        if (DestroyWhenDrop)
            ConsumeGroupItems(quest);
    }

    private void ConsumeGroupItems(Quest quest)
    {
        var remaining = Math.Min(GetObjective(quest), MaxObjective());
        if (remaining <= 0 || quest.Owner == null)
            return;
        foreach (var itemId in QuestManager.Instance.GetGroupItems(ItemGroupId))
        {
            if (remaining <= 0)
                break;
            var have = quest.Owner.Inventory.GetItemsCount(itemId);
            if (have <= 0)
                continue;
            var take = Math.Min(have, remaining);
            quest.Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, itemId, take, null);
            remaining -= take;
        }
    }
}
