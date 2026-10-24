using System.Collections.Generic;
using Verse;

namespace DMS.Hegemony
{
    /// <summary>
    /// 掛在 FactionDef 上:這個派系的 Hegemony AI 聚落只會規劃與製造這份名單裡的機兵。
    /// 名單是生產比例(權重),不是固定數量;實際數量仍由 Hegemony 的頻寬與材料決定。
    /// On a FactionDef: that faction's Hegemony AI towns only plan and build the chassis listed here.
    /// Weights are production proportions; Hegemony's bandwidth and materials still decide counts.
    /// </summary>
    public class MechRosterExtension : DefModExtension
    {
        public List<MechRosterOption> roster = new List<MechRosterOption>();

        [Unsaved]
        private HashSet<PawnKindDef> kinds;

        public bool Allows(PawnKindDef kind)
        {
            if (kind == null)
            {
                return false;
            }
            if (kinds == null)
            {
                kinds = new HashSet<PawnKindDef>();
                foreach (MechRosterOption option in roster)
                {
                    if (option?.kind != null && option.weight > 0)
                    {
                        kinds.Add(option.kind);
                    }
                }
            }
            return kinds.Contains(kind);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (roster.NullOrEmpty())
            {
                yield return "MechRosterExtension has an empty roster.";
                yield break;
            }
            foreach (MechRosterOption option in roster)
            {
                if (option?.kind == null)
                {
                    yield return "MechRosterExtension has an entry without a kind.";
                }
                else if (option.weight < 1)
                {
                    yield return "MechRosterExtension entry " + option.kind.defName + " needs a weight of at least 1.";
                }
            }
        }
    }

    public class MechRosterOption
    {
        public PawnKindDef kind;

        public int weight = 1;
    }
}
