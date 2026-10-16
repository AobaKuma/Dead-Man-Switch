using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 研究完成時派發一條任務線。掛在 <see cref="ResearchProjectDef"/> 上，
    /// 例如量子位元破譯 → 石碑任務線（DMS_Stele）。
    /// </summary>
    public class ModExtension_ResearchQuest : DefModExtension
    {
        public QuestScriptDef quest;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (quest == null)
            {
                yield return "quest is null";
            }
        }
    }

    public static class ResearchQuestUtility
    {
        /// <summary>
        /// 研究完成時的入口（由 <see cref="Patch_ResearchManager_FinishProject"/> 呼叫）。
        /// 每條任務線終生只派發一次：任務管理器裡只要有過同 root 的任務（含已結束的歷史任務）就不再派發，
        /// 因此不需要額外的存檔欄位，重複 FinishProject 也不會重發。
        /// </summary>
        public static void Notify_ProjectFinished(ResearchProjectDef proj)
        {
            QuestScriptDef root = proj.GetModExtension<ModExtension_ResearchQuest>()?.quest;
            if (root == null)
            {
                return;
            }
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            if (Find.QuestManager.QuestsListForReading.Any(q => q.root == root))
            {
                return;
            }
            Map map = Find.AnyPlayerHomeMap;
            float points = map != null
                ? StorytellerUtility.DefaultThreatPointsNow(map)
                : StorytellerUtility.DefaultThreatPointsNow(Find.World);
            QuestUtility.GenerateQuestAndMakeAvailable(root, points);
        }
    }
}
