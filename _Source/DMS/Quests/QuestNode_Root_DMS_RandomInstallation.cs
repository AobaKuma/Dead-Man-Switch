using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace DMS
{
    /// <summary>
    /// 一種可抽到的設施：站點定義、權重、最低威脅點數，以及專屬的逾期／肅清信件鍵。
    /// One installation the quest can roll: its SitePartDef, weight, points floor and its own expiry / cleared letter keys.
    /// </summary>
    public class DMS_InstallationOption
    {
        public SitePartDef sitePartDef;
        public float weight = 1f;

        /// <summary>低於這個威脅點數就不會抽到；讓重型設施留到中後期。Not rolled below this many points, so heavy installations wait for the mid game.</summary>
        public float minPoints = 0f;

        /// <summary>
        /// 寫進任務名稱／描述語法常數 installation 的值，留空用 sitePartDef.defName。
        /// 規則字串靠它分流：questDescription(installation==DMS_VaultStationSite)->…
        /// Value of the grammar constant "installation" for name / description rules; defaults to sitePartDef.defName.
        /// </summary>
        [NoTranslate] public string tag;

        [NoTranslate] public string expiredLetterLabelKey;
        [NoTranslate] public string expiredLetterTextKey;
        [NoTranslate] public string clearedLetterLabelKey;
        [NoTranslate] public string clearedLetterTextKey;
    }

    /// <summary>
    /// 從 <see cref="installations"/> 依權重抽一座設施，再交給 FFF 的限時敵對站點根節點生成。
    /// 用一個 QuestScriptDef 涵蓋多種站點，任務池裡就不會被同一類任務塞滿；
    /// 名稱與描述靠語法常數 installation 分流，信件鍵則逐項覆寫父類欄位。
    ///
    /// Rolls one installation from <see cref="installations"/> by weight and hands it to FFF's timed hostile
    /// site root. One QuestScriptDef covers several site kinds so the quest pool isn't flooded with near-identical
    /// entries; name and description branch on the grammar constant "installation", and the letter keys override
    /// the base node's fields per pick.
    /// </summary>
    public class QuestNode_Root_DMS_RandomInstallation : Fortified.QuestNode_Root_FFF_TimedHostileSite
    {
        public List<DMS_InstallationOption> installations;

        private const string InstallationConstant = "installation";

        private IEnumerable<DMS_InstallationOption> Eligible(float points)
        {
            if (installations == null) yield break;
            for (int i = 0; i < installations.Count; i++)
            {
                DMS_InstallationOption o = installations[i];
                if (o?.sitePartDef != null && o.weight > 0f && points >= o.minPoints) yield return o;
            }
        }

        private static float PointsFrom(Slate slate)
        {
            return slate.Get("points", StorytellerUtility.DefaultSiteThreatPointsNow());
        }

        /// <summary>
        /// 父類的 TestRunInt 需要 sitePartDef 非空，這裡先借第一個合格項目填上；不在測試階段擲骰。
        /// The base test needs a non-null sitePartDef; borrow the first eligible one. No Rand during testing.
        /// </summary>
        protected override bool TestRunInt(Slate slate)
        {
            DMS_InstallationOption first = Eligible(PointsFrom(slate)).FirstOrDefault();
            if (first == null) return false;
            sitePartDef = first.sitePartDef;
            return base.TestRunInt(slate);
        }

        protected override void RunInt()
        {
            if (!Eligible(PointsFrom(QuestGen.slate)).TryRandomElementByWeight(o => o.weight, out DMS_InstallationOption pick))
            {
                Log.Error("[DMS] QuestNode_Root_DMS_RandomInstallation: no eligible installation; quest generation aborted.");
                return;
            }

            sitePartDef = pick.sitePartDef;
            if (!pick.expiredLetterLabelKey.NullOrEmpty()) expiredLetterLabelKey = pick.expiredLetterLabelKey;
            if (!pick.expiredLetterTextKey.NullOrEmpty()) expiredLetterTextKey = pick.expiredLetterTextKey;
            if (!pick.clearedLetterLabelKey.NullOrEmpty()) clearedLetterLabelKey = pick.clearedLetterLabelKey;
            if (!pick.clearedLetterTextKey.NullOrEmpty()) clearedLetterTextKey = pick.clearedLetterTextKey;

            string tag = pick.tag.NullOrEmpty() ? pick.sitePartDef.defName : pick.tag;
            QuestGen.AddQuestNameConstants(new Dictionary<string, string> { { InstallationConstant, tag } });
            QuestGen.AddQuestDescriptionConstants(new Dictionary<string, string> { { InstallationConstant, tag } });

            base.RunInt();
        }
    }
}
