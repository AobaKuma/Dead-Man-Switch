using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace DMS
{
    /// <summary>
    /// 在「世界上實際存在的」派系中隨機挑一個，再交給 FFF 的限時敵對站點節點處理。
    ///
    /// 起因：香草的 Pirate 派系在裝了 Biotech 之後不會生成，實際出現的是
    /// PirateWaster / PirateYttakin 之類的異種人海盜；把 factionDef 寫死在 XML 裡
    /// 會讓 TestRunInt 的 FirstFactionOfDef 查不到實體，任務永遠不會出現。
    /// 這裡改成執行期挑選，順帶也吃得到其他模組新增的海盜派系。
    ///
    /// Picks a faction that actually exists in the current world, then defers to
    /// FFF's timed hostile site node. Hard-coding factionDef breaks under Biotech,
    /// where the vanilla Pirate faction never generates and the xenotype pirate
    /// factions take its place; resolving at runtime also picks up pirate factions
    /// added by other mods.
    ///
    /// 挑選順序 / Selection order:
    ///   1. factionDefs 白名單中、世界上確實有實體的派系（維持任務敘述的調性）
    ///   2. 白名單全部落空時，若 autoDiscoverPermanentEnemies 為 true，
    ///      退回「永久敵對的人形派系」——實務上就是各家海盜
    /// </summary>
    public class QuestNode_Root_DMS_RandomFactionTimedSite : Fortified.QuestNode_Root_FFF_TimedHostileSite
    {
        /// <summary>
        /// 優先使用的派系白名單。留空則直接走自動探索。
        /// Preferred faction pool. Leave empty to go straight to auto-discovery.
        /// </summary>
        public List<FactionDef> factionDefs;

        /// <summary>
        /// 白名單全數落空時，是否退回「永久敵對的人形派系」。
        /// Fall back to permanently-hostile humanlike factions when the pool is empty.
        /// </summary>
        public bool autoDiscoverPermanentEnemies = true;

        /// <summary>
        /// 是否只挑目前對玩家敵對的派系。
        /// Restrict to factions currently hostile to the player.
        /// </summary>
        public bool mustBeHostileToPlayer = true;

        /// <summary>
        /// 基本可用性：還在世界上、不是玩家、不是暫時派系、不是隱藏派系。
        /// Baseline usability, independent of which selection mode we're in.
        /// </summary>
        private bool IsUsable(Faction f)
        {
            if (f == null || f.def == null) return false;
            if (f.defeated || f.temporary || f.IsPlayer || f.def.hidden) return false;

            if (mustBeHostileToPlayer)
            {
                Faction player = Faction.OfPlayerSilentFail;
                if (player != null && !f.HostileTo(player)) return false;
            }

            return true;
        }

        /// <summary>白名單命中的派系。Factions matching the explicit pool.</summary>
        private IEnumerable<Faction> PreferredFactions()
        {
            if (factionDefs.NullOrEmpty()) yield break;

            List<Faction> all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (IsUsable(all[i]) && factionDefs.Contains(all[i].def)) yield return all[i];
            }
        }

        /// <summary>
        /// 自動探索：永久敵對的人形派系。
        /// 香草／Biotech／各家模組的海盜都符合；機械體與蟲族因為 humanlikeFaction
        /// 為 false 被排除，古代敵人之流則因為 hidden 被排除。
        ///
        /// Auto-discovery: permanently hostile humanlike factions. Catches pirates
        /// from vanilla, Biotech and other mods; mechanoids and insects are excluded
        /// by humanlikeFaction, hidden ancients by def.hidden.
        /// </summary>
        private IEnumerable<Faction> DiscoveredFactions()
        {
            if (!autoDiscoverPermanentEnemies) yield break;

            List<Faction> all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                Faction f = all[i];
                if (IsUsable(f) && f.def.permanentEnemy && f.def.humanlikeFaction) yield return f;
            }
        }

        private IEnumerable<Faction> Candidates()
        {
            List<Faction> preferred = PreferredFactions().ToList();
            return preferred.Count > 0 ? preferred : DiscoveredFactions();
        }

        /// <summary>
        /// 父類的 TestRunInt 會檢查寫死的 factionDef，這裡改成檢查「有沒有任何候選」。
        /// 刻意不在測試階段動用 Rand —— 這個方法在任務挑選期間會被反覆呼叫。
        ///
        /// The base checks its hard-coded factionDef; we check whether any candidate
        /// exists instead. Deliberately no Rand here — this runs repeatedly during
        /// quest selection.
        /// </summary>
        protected override bool TestRunInt(Slate slate)
        {
            if (sitePartDef == null) return false;
            if (!Candidates().Any()) return false;
            return TileFinder.TryFindNewSiteTile(out _, exitOnFirstTileFound: true);
        }

        /// <summary>
        /// 真正生成時才擲骰決定派系，寫回父類的 factionDef 後交給它處理。
        /// 父類接著會用 FirstFactionOfDef 取回實體，所以這裡挑的是 def 而不是實體。
        ///
        /// Roll for real at generation time and hand the result to the base node.
        /// The base resolves it back through FirstFactionOfDef, so we pick a def.
        /// </summary>
        protected override void RunInt()
        {
            List<FactionDef> pool = Candidates().Select(f => f.def).Distinct().ToList();
            if (pool.Count == 0)
            {
                Log.Error("[DMS] QuestNode_Root_DMS_RandomFactionTimedSite: no usable faction for "
                          + (sitePartDef?.defName ?? "site") + "; quest generation aborted.");
                return;
            }

            factionDef = pool.RandomElement();
            base.RunInt();
        }
    }
}
