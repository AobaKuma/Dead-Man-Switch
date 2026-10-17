using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace DMS
{
    /// <summary>
    /// 追緝網路節點站（SAGE 節點）：只在隱匿級追殺進行中、且未暫停時可以生成，同時最多一份。
    /// 其餘全部沿用 RandomInstallation（依點數抽站點、信件鍵覆寫、installation 語法常數），
    /// 另外提供語法常數 fleetName 讓描述能直呼艦隊的名字。
    ///
    /// Tracking-network sites (SAGE nodes): only while the Occulted hunt is running and not suspended, one at a
    /// time. Everything else is RandomInstallation's (roll by points, letter keys, the "installation" constant);
    /// adds the grammar constant fleetName so descriptions can name the fleet.
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
            Dictionary<string, string> constants = new Dictionary<string, string> { { "fleetName", fleetName } };
            QuestGen.AddQuestNameConstants(constants);
            QuestGen.AddQuestDescriptionConstants(constants);
            base.RunInt();
        }

        public static bool HuntRunning()
        {
            GameComponent_OccultechSanction c = GameComponent_OccultechSanction.CompSafe;
            return c != null && c.PermanentHostile && !c.HuntSuspended;
        }

        private static bool AlreadyOffered()
        {
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;
            return quests != null && quests.Any(q => q.root == DMS_DefOf.DMS_FleetNetworkSite && !q.Historical);
        }
    }
}
