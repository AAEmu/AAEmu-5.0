using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportNpcGroup(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestMonsterGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest {quest.TemplateId}, GroupId {QuestMonsterGroupId}");
        if (questAct.OverrideObjectiveCompleted)
            return true;
        if (quest.Owner.CurrentTarget is not Npc npc)
            return false;
        return QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, npc.TemplateId);
    }

    public override void InitializeQuest(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnReportNpc += questAct.OnReportNpc;
    }

    public override void FinalizeQuest(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnReportNpc -= questAct.OnReportNpc;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnReportNpc(QuestAct questAct, object sender, OnReportNpcArgs args)
    {
        if (questAct.Id != ActId)
            return;
        if (!QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, args.NpcId))
            return;

        var minimumProgress = questAct.Template.ParentComponent.ParentQuestTemplate.LetItDone
            ? QuestObjectiveStatus.CanEarlyComplete
            : QuestObjectiveStatus.QuestComplete;
        var isReady = questAct.QuestComponent.Parent.Parent.GetQuestObjectiveStatus() >= minimumProgress;
        if (!isReady)
            return;

        questAct.QuestComponent.Parent.Parent.SelectedRewardIndex = args.Selected;
        questAct.OverrideObjectiveCompleted = true;
        if (questAct.QuestComponent.Parent.Parent.Step <= QuestComponentKind.Progress)
            questAct.QuestComponent.Parent.Parent.Step = QuestComponentKind.Ready;
        questAct.RequestEvaluation();
    }
}
