using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Use a number of items where any item in the configured quest item group counts.
/// Mirrors QuestActObjItemUse but matches an item group (item_group_id) instead of a single ItemId.
/// </summary>
public class QuestActObjItemGroupUse(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint ItemGroupId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest {quest.TemplateId}, ItemGroupId {ItemGroupId}, Count {currentObjectiveCount}/{Count}");
        return currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnItemUse += questAct.OnItemUse;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnItemUse -= questAct.OnItemUse;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnItemUse(QuestAct questAct, object sender, OnItemUseArgs args)
    {
        if (questAct.Id != ActId)
            return;
        if (!QuestManager.Instance.CheckGroupItem(ItemGroupId, args.ItemId))
            return;
        AddObjective(questAct, 1);
    }
}
