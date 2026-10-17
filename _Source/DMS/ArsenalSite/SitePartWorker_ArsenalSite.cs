using System.Collections.Generic;
using Fortified;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;

namespace DMS
{
    /// <summary>
    /// 軍火庫舊址的站點：限時敵對站點的行為照舊（進場倒數、增援），但守軍是巡視的守墓人分隊而不是派系守軍，
    /// 所以任務描述的 [guardCount] / [guardLabel] 與世界地圖的威脅標籤改用 <see cref="ModExtension_ArsenalSite"/> 算出的架數。
    ///
    /// The old arsenal site: behaves like any timed hostile site (entry countdown, reinforcements), but its garrison is
    /// the patrolling gravekeeper squad rather than a faction group, so the quest text's [guardCount] / [guardLabel] and
    /// the world-map threat label use the count from <see cref="ModExtension_ArsenalSite"/>.
    /// </summary>
    public class SitePartWorker_ArsenalSite : SitePartWorker_FFF_TimedHostileSite
    {
        private ModExtension_ArsenalSite Ext => def.GetModExtension<ModExtension_ArsenalSite>();

        public override void Notify_GeneratedByQuestGen(SitePart part, Slate slate, List<Rule> outExtraDescriptionRules, Dictionary<string, string> outExtraDescriptionConstants)
        {
            base.Notify_GeneratedByQuestGen(part, slate, outExtraDescriptionRules, outExtraDescriptionConstants);
            int count = GuardCount(part);
            outExtraDescriptionRules.Add(new Rule_String("guardCount", count.ToString()));
            outExtraDescriptionRules.Add(new Rule_String("guardLabel", GuardLabel(count)));
        }

        public override string GetPostProcessedThreatLabel(Site site, SitePart sitePart)
        {
            if (site.Faction != null && site.Faction.IsPlayer) return null;
            int count = GuardCount(sitePart);
            if (count <= 0) return def.label;
            return def.label + ": " + "KnownSiteThreatEnemyCountAppend".Translate(count, GuardLabel(count));
        }

        private int GuardCount(SitePart part)
        {
            return Ext?.GuardCountFor(part.parms.threatPoints) ?? 0;
        }

        private string GuardLabel(int count)
        {
            PawnKindDef kind = Ext?.LabelKind;
            if (kind == null) return count == 1 ? "Enemy".Translate() : "Enemies".Translate();
            return count == 1 ? kind.label : kind.GetLabelPlural(count);
        }
    }
}
