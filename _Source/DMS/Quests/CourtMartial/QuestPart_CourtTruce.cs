using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 休戰與外交結算:
    /// - 啟用(接受任務)時:艦隊→中立(好感度歸零)。
    /// - 期間監控:若關係再度轉為敵對(玩家挑釁),發出 outSignalTruceBroken。
    /// - 收到 inSignalSuccess(被告歸還):按被告社交/交談能力擲盟友判定,
    ///   成功→盟友(好感度 allyGoodwill),失敗→維持中立;發出結案信件。
    /// - 收到 inSignalFail(背叛/被告死亡/休戰破裂):恢復敵對,發出信件。
    /// </summary>
    public class QuestPart_CourtTruce : QuestPartActivable
    {
        public Faction faction;
        public Pawn defendant;
        public string inSignalSuccess;
        public string inSignalAcquitted;   // 無罪釋放:不擲骰,直接盟友
        public string inSignalFail;
        public string outSignalTruceBroken;

        public float baseAllyChance = 0.15f;
        public float allyChancePerSocialLevel = 0.03f;
        public int allyGoodwill = 75;

        private bool resolved;

        private const int CheckIntervalTicks = 250;

        protected override void Enable(SignalArgs receivedArgs)
        {
            base.Enable(receivedArgs);
            SetGoodwillAndRelation(0, FactionRelationKind.Neutral);
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (resolved || faction == null) return;
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0) return;
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
            {
                // 玩家在休戰期間再次挑起敵對
                Find.SignalManager.SendSignal(new Signal(outSignalTruceBroken));
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (resolved) return;

            if (inSignalAcquitted != null && signal.tag == inSignalAcquitted)
            {
                // 無罪釋放:直接盟友(信件由宣判 part 發出)
                resolved = true;
                SetGoodwillAndRelation(allyGoodwill, FactionRelationKind.Ally);
                Complete();
                return;
            }
            if (signal.tag == inSignalSuccess)
            {
                resolved = true;
                bool ally = Rand.Chance(AllyChance);
                if (ally)
                    SetGoodwillAndRelation(allyGoodwill, FactionRelationKind.Ally);
                else
                    SetGoodwillAndRelation(0, FactionRelationKind.Neutral);

                Find.LetterStack.ReceiveLetter(
                    SupplyChainText.Resolve(CourtMartialText.Pack,
                        ally ? "returnAllyLetterLabel" : "returnNeutralLetterLabel", LetterVars()),
                    SupplyChainText.Resolve(CourtMartialText.Pack,
                        ally ? "returnAllyLetterText" : "returnNeutralLetterText", LetterVars()),
                    ally ? LetterDefOf.PositiveEvent : LetterDefOf.NeutralEvent,
                    defendant, faction, quest);
                Complete();
            }
            else if (signal.tag == inSignalFail)
            {
                resolved = true;
                SetGoodwillAndRelation(-100, FactionRelationKind.Hostile);
                Find.LetterStack.ReceiveLetter(
                    SupplyChainText.Resolve(CourtMartialText.Pack, "betrayLetterLabel", LetterVars()),
                    SupplyChainText.Resolve(CourtMartialText.Pack, "betrayLetterText", LetterVars()),
                    LetterDefOf.NegativeEvent, null, faction, quest);
                Complete();
            }
        }

        public float AllyChance
        {
            get
            {
                if (defendant == null || defendant.Dead) return 0f;
                int social = defendant.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                float talking = defendant.health?.capacities?.GetLevel(PawnCapacityDefOf.Talking) ?? 0f;
                return Mathf.Clamp01((baseAllyChance + allyChancePerSocialLevel * social) * talking);
            }
        }

        private string[] LetterVars()
        {
            return new[]
            {
                "defendantName", defendant?.LabelShort ?? "?",
                "issuerFactionName", faction?.Name ?? "?",
            };
        }

        private void SetGoodwillAndRelation(int targetGoodwill, FactionRelationKind kind)
        {
            if (faction == null) return;
            Faction player = Faction.OfPlayer;
            int delta = targetGoodwill - faction.GoodwillWith(player);
            if (delta != 0)
                faction.TryAffectGoodwillWith(player, delta, false, false, null, null);
            if (faction.RelationKindWith(player) != kind)
                faction.SetRelationDirect(player, kind, false, null, null);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref faction, "faction");
            Scribe_References.Look(ref defendant, "defendant");
            Scribe_Values.Look(ref inSignalSuccess, "inSignalSuccess");
            Scribe_Values.Look(ref inSignalAcquitted, "inSignalAcquitted");
            Scribe_Values.Look(ref inSignalFail, "inSignalFail");
            Scribe_Values.Look(ref outSignalTruceBroken, "outSignalTruceBroken");
            Scribe_Values.Look(ref baseAllyChance, "baseAllyChance", 0.15f);
            Scribe_Values.Look(ref allyChancePerSocialLevel, "allyChancePerSocialLevel", 0.03f);
            Scribe_Values.Look(ref allyGoodwill, "allyGoodwill", 75);
            Scribe_Values.Look(ref resolved, "resolved");
        }
    }
}
