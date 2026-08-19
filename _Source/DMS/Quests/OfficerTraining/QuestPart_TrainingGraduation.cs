using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 完訓結算(受訓天數到期觸發):
    /// 晉升 → 灌課程技能經驗(可能升熱情)→ 隨機移除一個負面特質 → 發完訓信件 →
    /// 令 lend part Complete() 空投歸還(其完成訊號驅動任務成功)。
    ///
    /// 沒有負面特質可移除時改走 graduateNoTraitLetter,並用額外經驗補償,
    /// 免得「兵素質太好」反而虧。
    /// </summary>
    public class QuestPart_TrainingGraduation : QuestPart
    {
        public string inSignal;
        public Faction faction;
        public Pawn trainee;
        public RoyalTitleDef newTitle;
        public SkillDef courseSkill;
        public string courseName;
        public float skillXp = 35000f;
        public float passionUpgradeChance = 0.25f;
        public float noTraitBonusXpFactor = 0.5f;
        public string outSignalFail;

        private bool done;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || done) return;
            done = true;

            if (trainee == null || trainee.Dead || faction == null)
            {
                if (!outSignalFail.NullOrEmpty())
                    Find.SignalManager.SendSignal(new Signal(outSignalFail));
                return;
            }

            // ---- 1. 晉升 ----------------------------------------------------
            RoyalTitleDef current = trainee.royalty?.GetCurrentTitle(faction);
            string oldTitleLabel = current?.GetLabelFor(trainee)
                                   ?? "DMS_OfficerTraining_NoTitle".Translate().ToString();

            RoyalTitleDef target = newTitle;
            if (trainee.royalty != null && (target == null || (current != null && current.seniority >= target.seniority)))
            {
                // 受訓期間榮譽又漲了,或目標階級已經拿到手 —— 重算一次
                target = trainee.royalty.GetTitleAwardedWhenUpdating(faction, trainee.royalty.GetFavor(faction));
            }
            if (trainee.royalty != null && target != null && (current == null || target.seniority > current.seniority))
            {
                // 榮譽是累積制,晉升不用扣;信件我們自己發,所以 sendLetter = false
                trainee.royalty.SetTitle(faction, target, grantRewards: true,
                    rewardsOnlyForNewestTitle: true, sendLetter: false);
            }
            string newTitleLabel = trainee.royalty?.GetCurrentTitle(faction)?.GetLabelFor(trainee) ?? oldTitleLabel;

            // ---- 2. 移除負面特質 --------------------------------------------
            bool removedTrait = TryRemoveNegativeTrait(out string traitLabel);

            // ---- 3. 課程技能經驗 --------------------------------------------
            float xp = skillXp * (removedTrait ? 1f : 1f + Mathf.Max(0f, noTraitBonusXpFactor));
            SkillRecord record = (courseSkill != null) ? trainee.skills?.GetSkill(courseSkill) : null;
            if (record != null && !record.TotallyDisabled)
            {
                record.Learn(xp, direct: true, ignoreLearnRate: true);   // 課程獎勵是定額,不吃學習速度倍率
                if (record.passion == Passion.None)
                {
                    if (Rand.Chance(passionUpgradeChance)) record.passion = Passion.Minor;
                }
                else if (record.passion == Passion.Minor)
                {
                    if (Rand.Chance(passionUpgradeChance * 0.5f)) record.passion = Passion.Major;
                }
            }

            // ---- 4. 完訓信件 -------------------------------------------------
            string[] vars =
            {
                "traineeName", trainee.LabelShort,
                "issuerFactionName", faction.Name ?? "?",
                "oldTitle", oldTitleLabel,
                "newTitle", newTitleLabel,
                "courseName", courseName ?? "?",
                "skillName", courseSkill?.LabelCap.ToString() ?? "?",
                "traitName", traitLabel ?? "?",
            };
            string key = removedTrait ? "graduateLetter" : "graduateNoTraitLetter";
            Find.LetterStack.ReceiveLetter(
                SupplyChainText.Resolve(OfficerTrainingText.Pack, key + "Label", vars),
                SupplyChainText.Resolve(OfficerTrainingText.Pack, key + "Text", vars),
                LetterDefOf.PositiveEvent, trainee, faction, quest);

            // ---- 5. 歸還 -----------------------------------------------------
            QuestPart_LendColonistsToFaction lend = quest.PartsListForReading
                .OfType<QuestPart_LendColonistsToFaction>()
                .FirstOrDefault();
            if (lend != null && lend.State == QuestPartState.Enabled)
                lend.Complete(new SignalArgs());   // 明確使用 Complete(SignalArgs) 多載(經 Publicizer 開放)
        }

        /// <summary>
        /// 挑一個負面特質移除。名單與啟發式開關都讀自 QuestScriptDef 的 root 節點,
        /// 所以不需要把設定塞進存檔。
        /// </summary>
        private bool TryRemoveNegativeTrait(out string label)
        {
            label = null;
            TraitSet traits = trainee.story?.traits;
            if (traits == null) return false;

            QuestNode_Root_OfficerTraining config = QuestNode_Root_OfficerTraining.Config;
            List<TraitEntry> whitelist = config?.removableTraits;
            bool heuristic = config?.alsoRemoveNegativeValueTraits ?? false;

            List<Trait> pool = new List<Trait>();
            foreach (Trait t in traits.allTraits)
            {
                if (t?.def == null) continue;
                if (t.sourceGene != null) continue;   // 基因來源:RemoveTrait 會連基因一起拔掉
                if (t.Suppressed || t.ScenForced) continue;
                TraitDegreeData data = t.CurrentData;
                if (data == null) continue;

                bool listed = TraitEntry.AnyMatch(whitelist, t);
                bool negativeValue = heuristic && data.marketValueFactorOffset < 0f;
                if (listed || negativeValue) pool.Add(t);
            }

            if (pool.Count == 0) return false;

            Trait pick = pool.RandomElement();
            label = pick.LabelCap;
            traits.RemoveTrait(pick, unsuppressConflicts: true);
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_References.Look(ref faction, "faction");
            Scribe_References.Look(ref trainee, "trainee");
            Scribe_Defs.Look(ref newTitle, "newTitle");
            Scribe_Defs.Look(ref courseSkill, "courseSkill");
            Scribe_Values.Look(ref courseName, "courseName");
            Scribe_Values.Look(ref skillXp, "skillXp", 35000f);
            Scribe_Values.Look(ref passionUpgradeChance, "passionUpgradeChance", 0.25f);
            Scribe_Values.Look(ref noTraitBonusXpFactor, "noTraitBonusXpFactor", 0.5f);
            Scribe_Values.Look(ref outSignalFail, "outSignalFail");
            Scribe_Values.Look(ref done, "done");
        }
    }
}
