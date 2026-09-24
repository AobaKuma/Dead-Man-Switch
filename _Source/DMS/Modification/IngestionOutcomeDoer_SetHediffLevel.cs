using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 服用後讓指定 Hediff_Level 系列 hediff 直接到達某個等級（例如戰鬥再生合劑的排異反應 → 異肢症 LV.3）。
    /// 尚未患病時在 partsToAffect 中的第一個可用部位新增，已患病且等級較低時升到該等級，已經更高則不變。
    /// 觸發機率沿用 IngestionOutcomeDoer.chance。
    /// 原版 IngestionOutcomeDoer_GiveHediff 不適用：Hediff_Level 每個 tick 都會把 Severity 覆寫回 level，
    /// 而且沒有部位時 PostAdd 會報錯。
    /// </summary>
    public class IngestionOutcomeDoer_SetHediffLevel : IngestionOutcomeDoer
    {
        public HediffDef hediffDef;

        public int level = 1;

        public List<BodyPartDef> partsToAffect;

        /// <summary>觸發時對殖民者發出的訊息 key，{PAWN} 為角色、{0} 為含分級的病症名稱；留空則不發訊息。</summary>
        public string messageKey;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            Hediff_Level hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef) as Hediff_Level;
            if (hediff == null)
            {
                BodyPartRecord part = FindPart(pawn);
                if (part == null)
                {
                    return;
                }
                hediff = (Hediff_Level)HediffMaker.MakeHediff(hediffDef, pawn, part);
                hediff.level = level;
                hediff.Severity = level;
                pawn.health.AddHediff(hediff, part);
            }
            else if (hediff.level < level)
            {
                hediff.SetLevelTo(level);
                // 立即同步 Severity，讓 stage 在同一個 tick 生效。
                hediff.Severity = hediff.level;
            }
            else
            {
                return;
            }
            if (!messageKey.NullOrEmpty() && PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Messages.Message(messageKey.Translate(pawn.Named("PAWN"), hediff.LabelCap),
                    pawn, MessageTypeDefOf.NegativeHealthEvent);
            }
        }

        private BodyPartRecord FindPart(Pawn pawn)
        {
            if (partsToAffect.NullOrEmpty())
            {
                return pawn.RaceProps.body.corePart;
            }
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (partsToAffect.Contains(part.def))
                {
                    return part;
                }
            }
            return null;
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(ThingDef parentDef)
        {
            if (!parentDef.IsDrug)
            {
                yield break;
            }
            yield return new StatDrawEntry(StatCategoryDefOf.Drug,
                "DMS_SetHediffLevel_StatLabel".Translate(hediffDef.LabelCap),
                "DMS_SetHediffLevel_StatValue".Translate(level, chance.ToStringPercent()),
                "DMS_SetHediffLevel_StatDesc".Translate(hediffDef.label, level, chance.ToStringPercent()),
                1000);
        }
    }
}
