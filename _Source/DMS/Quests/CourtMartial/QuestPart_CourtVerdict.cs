using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 宣判(調查期滿觸發):
    /// - 先擲無罪判定:機率由官階 seniority 與社交/交談能力決定。
    ///   無罪 → 不降職,立即令 lend part 歸還被告,發 outSignalAcquitted。
    ///   有罪 → 降一級官階,發 outSignalGuilty(啟動服刑倒數),發判決信。
    /// </summary>
    public class QuestPart_CourtVerdict : QuestPart
    {
        public string inSignal;
        public Faction faction;
        public Pawn defendant;
        public int detentionDays;
        public string outSignalAcquitted;
        public string outSignalGuilty;

        // 無罪機率參數(XML 經 node 傳入)
        public float baseAcquitChance = 0.10f;
        public float acquitChancePerSocialLevel = 0.02f;
        public float acquitChancePerSeniority100 = 0.05f;
        public float maxAcquitChance = 0.75f;

        private bool done;

        public float AcquitChance
        {
            get
            {
                if (defendant == null || defendant.Dead) return 0f;
                int social = defendant.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                float talking = defendant.health?.capacities?.GetLevel(PawnCapacityDefOf.Talking) ?? 0f;
                int seniority = defendant.royalty?.GetCurrentTitle(faction)?.seniority ?? 0;
                float chance = baseAcquitChance
                    + acquitChancePerSocialLevel * social
                    + acquitChancePerSeniority100 * (seniority / 100f);
                return Mathf.Min(Mathf.Clamp01(chance) * talking, maxAcquitChance);
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || done) return;
            done = true;

            if (defendant == null || defendant.Dead || faction == null) return;

            if (Rand.Chance(AcquitChance))
            {
                // 無罪釋放:不降職,先讓休戰 part 收到無罪訊號(直接盟友),再提前歸還
                Find.LetterStack.ReceiveLetter(
                    SupplyChainText.Resolve(CourtMartialText.Pack, "acquitLetterLabel", LetterVars("", "")),
                    SupplyChainText.Resolve(CourtMartialText.Pack, "acquitLetterText", LetterVars("", "")),
                    LetterDefOf.PositiveEvent, null, faction, quest);
                TaleRecorder.RecordTale(DMS_DefOf.DMS_Tale_Acquitted, defendant);
                Find.SignalManager.SendSignal(new Signal(outSignalAcquitted));
                ReturnDefendantNow();
                return;
            }

            // 有罪:降一級,進入服刑
            RoyalTitleDef current = defendant.royalty?.GetCurrentTitle(faction);
            string oldTitle = current?.GetLabelFor(defendant) ?? "?";
            if (current != null && current.seniority > 0)
                defendant.royalty.ReduceTitle(faction);
            string newTitle = defendant.royalty?.GetCurrentTitle(faction)?.GetLabelFor(defendant) ?? oldTitle;

            Find.LetterStack.ReceiveLetter(
                SupplyChainText.Resolve(CourtMartialText.Pack, "verdictLetterLabel", LetterVars(oldTitle, newTitle)),
                SupplyChainText.Resolve(CourtMartialText.Pack, "verdictLetterText", LetterVars(oldTitle, newTitle)),
                LetterDefOf.NeutralEvent, null, faction, quest);
            // 降階是一生一次的紀錄:TaleDef 用 Permanent + maxPerPawn 1,重複判決不會洗版。
            TaleRecorder.RecordTale(DMS_DefOf.DMS_Tale_CourtMartialed, defendant);
            // 刻意不帶 Doer:被告在服刑期間被 lend part 移出地圖,IdeoUtility.Notify_HistoryEvent
            // 只會把「知情」通知給跟 Doer 同地圖／同商隊的 pawn,帶了 Doer 反而全殖民地都收不到。
            // 不帶 Doer 時原版改走「通知所有自由殖民者」那條路,正好符合「消息傳回來了」的語意。
            Find.HistoryEventsManager.RecordEvent(new HistoryEvent(DMS_DefOf.DMS_MemberCourtMartialed));

            Find.SignalManager.SendSignal(new Signal(outSignalGuilty));
        }

        /// <summary>立即歸還被告:lend part 的 Complete() 會空投歸還並發出其完成訊號。</summary>
        private void ReturnDefendantNow()
        {
            QuestPart_LendColonistsToFaction lend = quest.PartsListForReading
                .OfType<QuestPart_LendColonistsToFaction>()
                .FirstOrDefault();
            if (lend != null && lend.State == QuestPartState.Enabled)
                lend.Complete(new SignalArgs());   // 明確使用 Complete(SignalArgs) 多載(經 Publicizer 開放)
        }

        private string[] LetterVars(string oldTitle, string newTitle)
        {
            return new[]
            {
                "defendantName", defendant?.LabelShort ?? "?",
                "issuerFactionName", faction?.Name ?? "?",
                "oldTitle", oldTitle,
                "newTitle", newTitle,
                "detentionDays", detentionDays.ToString(),
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_References.Look(ref faction, "faction");
            Scribe_References.Look(ref defendant, "defendant");
            Scribe_Values.Look(ref detentionDays, "detentionDays");
            Scribe_Values.Look(ref outSignalAcquitted, "outSignalAcquitted");
            Scribe_Values.Look(ref outSignalGuilty, "outSignalGuilty");
            Scribe_Values.Look(ref baseAcquitChance, "baseAcquitChance", 0.10f);
            Scribe_Values.Look(ref acquitChancePerSocialLevel, "acquitChancePerSocialLevel", 0.02f);
            Scribe_Values.Look(ref acquitChancePerSeniority100, "acquitChancePerSeniority100", 0.05f);
            Scribe_Values.Look(ref maxAcquitChance, "maxAcquitChance", 0.75f);
            Scribe_Values.Look(ref done, "done");
        }
    }

    /// <summary>服刑期滿:令 lend part 歸還被告(其完成訊號驅動後續結算)。</summary>
    public class QuestPart_ReturnDefendant : QuestPart
    {
        public string inSignal;
        private bool done;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || done) return;
            done = true;
            QuestPart_LendColonistsToFaction lend = quest.PartsListForReading
                .OfType<QuestPart_LendColonistsToFaction>()
                .FirstOrDefault();
            if (lend != null && lend.State == QuestPartState.Enabled)
                lend.Complete(new SignalArgs());   // 明確使用 Complete(SignalArgs) 多載(經 Publicizer 開放)
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref done, "done");
        }
    }
}
