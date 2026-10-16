using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 週期性發布一份「偏好模組」的隨機任務。挑選範圍與權重都沿用原版的隨機任務
    /// 規則，只是先把候選名單過濾成說書人 ModExtension_StorytellerBias 指定的來源。
    ///
    /// Fires one random quest per cycle, restricted to the mods listed in the
    /// storyteller's ModExtension_StorytellerBias. Candidate filtering and
    /// weighting reuse the vanilla random quest rules.
    /// </summary>
    public class StorytellerCompProperties_FavoredQuest : StorytellerCompProperties_OnOffCycle
    {
        public StorytellerCompProperties_FavoredQuest()
        {
            compClass = typeof(StorytellerComp_FavoredQuest);
        }
    }

    public class StorytellerComp_FavoredQuest : StorytellerComp_OnOffCycle
    {
        public override IncidentParms GenerateParms(IncidentCategoryDef incCat, IIncidentTarget target)
        {
            // 原版的 IncidentDef 不能同時指定 questScriptDef 又讓該任務留在隨機池裡
            // （見 IncidentDef.ConfigErrors），所以任務是在這裡塞進 parms 的。
            // A vanilla IncidentDef may not name a questScriptDef that is also in
            // the random pool, so the quest is supplied through parms instead.
            IncidentParms parms = base.GenerateParms(incCat, target);
            parms.questScriptDef = ChooseFavoredQuest(parms.points, target);
            return parms;
        }

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            foreach (FiringIncident incident in base.MakeIntervalIncidents(target))
            {
                // 這個週期沒有可發的偏好任務就整個跳過。放行的話
                // IncidentWorker_GiveQuest 會自己退回去抽一個原版任務，那不是我們要的。
                // Skip the cycle when nothing is eligible; letting it through would
                // make IncidentWorker_GiveQuest fall back to a vanilla quest.
                if (incident.parms.questScriptDef != null)
                {
                    yield return incident;
                }
            }
        }

        private static QuestScriptDef ChooseFavoredQuest(float points, IIncidentTarget target)
        {
            ModExtension_StorytellerBias bias = StorytellerBiasUtility.CurrentBias;
            if (bias == null)
            {
                return null;
            }
            DefDatabase<QuestScriptDef>.AllDefsListForReading
                .Where((QuestScriptDef quest) => quest.IsRootRandomSelected && bias.AppliesTo(quest) && quest.CanRun(points, target))
                .TryRandomElementByWeight((QuestScriptDef quest) => NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight(quest, points, target.StoryState), out QuestScriptDef result);
            return result;
        }
    }
}
