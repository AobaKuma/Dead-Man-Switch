using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;

namespace DMS
{
    /// <summary>
    /// 追緝網路節點站（SAGE 節點）：只在艦隊追殺（隱匿級追殺令或脫序者追殺）進行中、且不在真空期時可以生成，同時最多一份。
    /// 其餘全部沿用 RandomInstallation（依點數抽站點、信件鍵覆寫、installation 語法常數），
    /// 另外提供 fleetName 讓名稱與描述能直呼艦隊的名字。
    ///
    /// Tracking-network sites (SAGE nodes): only while a fleet hunt (the occultech kill order or the deviant hunt) is
    /// running outside a blackout, one at a time. Everything else is RandomInstallation's (roll by points, letter keys,
    /// the "installation" constant); adds fleetName so the name and description can name the fleet.
    /// </summary>
    public class QuestNode_Root_DMS_FleetNetworkSite : QuestNode_Root_DMS_RandomInstallation
    {
        /// <summary>除錯用：略過追殺判定。Debug: skip the hunt gate.</summary>
        public static bool ignoreHuntGate;

        protected override bool TestRunInt(Slate slate)
        {
            if (!ignoreHuntGate && !HuntRunning()) return false;
            if (AlreadyOffered()) return false;
            return base.TestRunInt(slate);
        }

        protected override void RunInt()
        {
            string fleetName = OccultechSanctionUtility.Fleet?.Name ?? "?";
            // 文字裡的 [fleetName] 需要規則；常數只能給規則條件判斷，單獨使用時描述會解析失敗（Grammar unresolvable）。
            // [fleetName] in the text needs a rule; a constant only feeds rule conditions, and alone the description
            // fails to resolve (Grammar unresolvable).
            List<Rule> rules = new List<Rule> { new Rule_String("fleetName", fleetName) };
            QuestGen.AddQuestNameRules(rules);
            QuestGen.AddQuestDescriptionRules(rules);
            Dictionary<string, string> constants = new Dictionary<string, string> { { "fleetName", fleetName } };
            QuestGen.AddQuestNameConstants(constants);
            QuestGen.AddQuestDescriptionConstants(constants);
            base.RunInt();
        }

        /// <summary>
        /// 有任一艦隊追殺（隱匿級追殺令或脫序者追殺）且追緝網路不在真空期。
        /// Any fleet hunt (the occultech kill order or the deviant hunt) is running and the tracking network isn't blacked out.
        /// </summary>
        public static bool HuntRunning()
        {
            return FleetTrackingUtility.AnyActive && !FleetTrackingUtility.InBlackout;
        }

        private static bool AlreadyOffered()
        {
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;
            return quests != null && quests.Any(q => q.root == DMS_DefOf.DMS_FleetNetworkSite && !q.Historical);
        }
    }
}
