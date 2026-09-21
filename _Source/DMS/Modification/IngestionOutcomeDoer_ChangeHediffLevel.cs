using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 服用後改變指定 Hediff_Level 系列 hediff 的等級（預設 -1 級）。
    /// 用於 Neuroglue 逐級壓低異肢癥候群；等級歸零時直接移除該 hediff。
    /// 若 pawn 身上沒有該 hediff 則什麼都不做。
    /// </summary>
    public class IngestionOutcomeDoer_ChangeHediffLevel : IngestionOutcomeDoer
    {
        public HediffDef hediffDef;

        public int levelOffset = -1;

        private static List<Hediff> tmpHediffs = new List<Hediff>();

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            // 同一種病可能長在多個部位，先收集再處理，避免在迭代中移除。
            tmpHediffs.Clear();
            pawn.health.hediffSet.GetHediffs(ref tmpHediffs, h => h.def == hediffDef && h is Hediff_Level);
            for (int i = 0; i < tmpHediffs.Count; i++)
            {
                Hediff_Level level = (Hediff_Level)tmpHediffs[i];
                level.ChangeLevel(levelOffset);
                if (level.level <= 0)
                {
                    pawn.health.RemoveHediff(level);
                    if (PawnUtility.ShouldSendNotificationAbout(pawn))
                    {
                        Messages.Message("DMS_BionicShock_Cured".Translate(pawn.Named("PAWN"), hediffDef.label),
                            pawn, MessageTypeDefOf.PositiveEvent);
                    }
                }
                else
                {
                    level.Severity = level.level;
                    if (levelOffset < 0 && PawnUtility.ShouldSendNotificationAbout(pawn))
                    {
                        Messages.Message("DMS_BionicShock_LevelDown".Translate(pawn.Named("PAWN"), level.LabelCap),
                            pawn, MessageTypeDefOf.PositiveEvent);
                    }
                }
            }
            tmpHediffs.Clear();
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(ThingDef parentDef)
        {
            if (!parentDef.IsDrug || !(chance >= 1f))
            {
                yield break;
            }
            yield return new StatDrawEntry(StatCategoryDefOf.Basics,
                "DMS_BionicShock_StatLabel".Translate(hediffDef.LabelCap),
                levelOffset.ToStringWithSign(),
                "DMS_BionicShock_StatDesc".Translate(hediffDef.label, levelOffset.ToStringWithSign()),
                1000);
        }
    }
}
