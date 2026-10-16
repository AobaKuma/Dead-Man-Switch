using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 戰鬥再生合劑的藥效：在 hediff 持續期間，把缺失的身體部位一個一個長回來。
    /// 每次長回的間隔 = 剩餘時間 / (剩餘缺失部位數 + 1)，所以會平均分散在整段藥效中，
    /// 最後一個部位也會在藥效結束前長好；藥效期間新失去的部位同樣會被排進去。
    /// 裝有義肢／仿生部位（或其上層部位裝有）的缺失不處理，避免把玩家裝好的仿生件一併移除。
    /// 需要同一個 hediff 上有 HediffComp_Disappears 來決定剩餘時間；沒有時退回固定間隔 fallbackIntervalTicks。
    /// </summary>
    public class HediffComp_RegrowMissingParts : HediffComp
    {
        private int ticksUntilNextRegrow = -1;

        private static List<Hediff_MissingPart> tmpMissing = new List<Hediff_MissingPart>();

        public HediffCompProperties_RegrowMissingParts Props => (HediffCompProperties_RegrowMissingParts)props;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksUntilNextRegrow, "ticksUntilNextRegrow", -1);
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            Pawn pawn = parent.pawn;
            if (pawn.Dead || !pawn.IsHashIntervalTick(Props.checkIntervalTicks, delta))
            {
                return;
            }

            int count = CollectRegrowable(pawn);
            Hediff_MissingPart next = count > 0 ? tmpMissing[0] : null;
            tmpMissing.Clear();
            if (count == 0)
            {
                // 沒有可再生的部位時重設排程，下次出現缺失時再依當下的剩餘時間重新分配。
                ticksUntilNextRegrow = -1;
                return;
            }
            if (ticksUntilNextRegrow < 0)
            {
                ticksUntilNextRegrow = NextInterval(count);
                return;
            }

            ticksUntilNextRegrow -= Props.checkIntervalTicks;
            if (ticksUntilNextRegrow > 0)
            {
                return;
            }

            Regrow(pawn, next);
            count = CollectRegrowable(pawn);
            ticksUntilNextRegrow = count > 0 ? NextInterval(count) : -1;
            tmpMissing.Clear();
        }

        private int NextInterval(int remainingParts)
        {
            HediffComp_Disappears disappears = parent.TryGetComp<HediffComp_Disappears>();
            if (disappears == null)
            {
                return Props.fallbackIntervalTicks;
            }
            return disappears.EffectiveTicksToDisappear / (remainingParts + 1);
        }

        /// <summary>收集可再生的缺失部位，依身體部位表的順序排列（靠近軀幹的部位通常在前）。</summary>
        private static int CollectRegrowable(Pawn pawn)
        {
            tmpMissing.Clear();
            HediffSet set = pawn.health.hediffSet;
            List<Hediff> hediffs = set.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_MissingPart missing && missing.Part != null
                    && !set.PartOrAnyAncestorHasDirectlyAddedParts(missing.Part))
                {
                    tmpMissing.Add(missing);
                }
            }
            tmpMissing.SortBy(m => m.Part.Index);
            return tmpMissing.Count;
        }

        private static void Regrow(Pawn pawn, Hediff_MissingPart missing)
        {
            BodyPartRecord part = missing.Part;
            pawn.health.RestorePart(part);
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Messages.Message("DMS_CombatRegen_PartRegrown".Translate(pawn.Named("PAWN"), part.Label),
                    pawn, MessageTypeDefOf.PositiveEvent);
            }
        }
    }

    public class HediffCompProperties_RegrowMissingParts : HediffCompProperties
    {
        /// <summary>檢查間隔（ticks）。</summary>
        public int checkIntervalTicks = 250;

        /// <summary>hediff 上沒有 HediffComp_Disappears 時，每長回一個部位的固定間隔（ticks）。</summary>
        public int fallbackIntervalTicks = 30000;

        public HediffCompProperties_RegrowMissingParts()
        {
            compClass = typeof(HediffComp_RegrowMissingParts);
        }
    }
}
