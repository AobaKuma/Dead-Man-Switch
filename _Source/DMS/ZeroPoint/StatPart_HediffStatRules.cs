using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 HediffDef 上：帶著這個 hediff 的 pawn，指定能力值先乘上 factor、再壓到 max 以下。
    /// 原版 HediffStage 只有 statFactors / statOffsets，沒有上限；上限要在所有加成都算完之後才套，
    /// 所以改成在啟動時把 <see cref="StatPart_HediffStatRules"/> 接到相關 StatDef 的 parts 最後面。
    /// stat 以 defName 字串指定、執行期才解析：DLC 的能力值（例如 Anomaly 的 MinimumContainmentStrength）
    /// 在沒有 DLC 時只是靜默略過，不會在載入 Def 時報交叉引用錯誤。
    /// On a HediffDef: for pawns carrying the hediff, the listed stats are multiplied by factor and then held at or
    /// below max. Vanilla HediffStage only has statFactors / statOffsets with no ceiling, and a ceiling has to apply
    /// after every other bonus, so at startup a <see cref="StatPart_HediffStatRules"/> is appended to the end of each
    /// affected StatDef's parts. Stats are named by defName and resolved at runtime, so a DLC stat (Anomaly's
    /// MinimumContainmentStrength, say) is silently skipped without the DLC instead of breaking cross-references.
    /// </summary>
    public class ModExtension_HediffStatRules : DefModExtension
    {
        public List<HediffStatRule> rules = new List<HediffStatRule>();
    }

    public class HediffStatRule
    {
        /// <summary>StatDef 的 defName。defName of the StatDef.</summary>
        public string stat;

        public float factor = 1f;

        public float max = float.MaxValue;
    }

    public class StatPart_HediffStatRules : StatPart
    {
        private readonly List<(HediffDef hediff, HediffStatRule rule)> entries = new List<(HediffDef, HediffStatRule)>();

        public void Add(HediffDef hediff, HediffStatRule rule) => entries.Add((hediff, rule));

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is Pawn pawn) || pawn.health?.hediffSet == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                (HediffDef hediff, HediffStatRule rule) = entries[i];
                if (!pawn.health.hediffSet.HasHediff(hediff)) continue;
                val *= rule.factor;
                if (val > rule.max) val = rule.max;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!(req.Thing is Pawn pawn) || pawn.health?.hediffSet == null) return null;
            StringBuilder sb = null;
            for (int i = 0; i < entries.Count; i++)
            {
                (HediffDef hediff, HediffStatRule rule) = entries[i];
                if (!pawn.health.hediffSet.HasHediff(hediff)) continue;
                sb ??= new StringBuilder();
                if (rule.factor != 1f)
                    sb.AppendLine(hediff.LabelCap + ": " + rule.factor.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Factor));
                if (rule.max < float.MaxValue)
                    sb.AppendLine("DMS_StatRule_Max".Translate(hediff.LabelCap, parentStat.ValueToString(rule.max)));
            }
            return sb?.ToString().TrimEndNewlines();
        }
    }

    [StaticConstructorOnStartup]
    public static class HediffStatRulesInjector
    {
        static HediffStatRulesInjector()
        {
            Dictionary<StatDef, StatPart_HediffStatRules> parts = new Dictionary<StatDef, StatPart_HediffStatRules>();
            foreach (HediffDef hediff in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                ModExtension_HediffStatRules ext = hediff.GetModExtension<ModExtension_HediffStatRules>();
                if (ext?.rules == null) continue;
                foreach (HediffStatRule rule in ext.rules)
                {
                    StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(rule.stat);
                    if (stat == null) continue;
                    if (!parts.TryGetValue(stat, out StatPart_HediffStatRules part))
                    {
                        part = new StatPart_HediffStatRules { parentStat = stat };
                        stat.parts ??= new List<StatPart>();
                        stat.parts.Add(part);
                        parts[stat] = part;
                    }
                    part.Add(hediff, rule);
                }
            }
        }
    }
}
