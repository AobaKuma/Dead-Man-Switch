using System;
using System.Collections.Generic;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 追殺間隔隨研究推進收緊。掛在 <see cref="HuntdownDef"/> 的 modExtensions 上；
    /// 沒掛這個設定的追殺完全不受影響（其他模組的 HuntdownDef 也不會被波及）。
    ///
    /// 未完成 <see cref="research"/> 時用 <see cref="intervalBefore"/>，完成後改用 <see cref="intervalAfter"/>。
    /// The hunt interval tightens as research progresses. Attached to a HuntdownDef's modExtensions; huntdowns without
    /// it are untouched (including other mods'). Before <see cref="research"/> is finished the hunt uses
    /// <see cref="intervalBefore"/>; afterwards it switches to <see cref="intervalAfter"/>.
    /// </summary>
    public class ModExtension_HuntIntervalByResearch : DefModExtension
    {
        /// <summary>門檻研究。未指定時整個設定不生效。</summary>
        public ResearchProjectDef research;

        /// <summary>完成前的間隔。預設 28~32 天。</summary>
        public IntRange intervalBefore = new IntRange(28 * GenDate.TicksPerDay, 32 * GenDate.TicksPerDay);

        /// <summary>完成後的間隔。預設 5~7 天。</summary>
        public IntRange intervalAfter = new IntRange(5 * GenDate.TicksPerDay, 7 * GenDate.TicksPerDay);
    }

    /// <summary>
    /// 把 <see cref="ModExtension_HuntIntervalByResearch"/> 套用到 HuntdownDef 的 raidDelayRange。
    ///
    /// Fortified 在每一次重新排程時才讀 <c>def.raidDelayRange</c>（一波打完後的下一波、暫停結束、
    /// 首次追蹤地圖），所以只要在研究完成與讀檔時把 def 上的值換掉就夠了：之後排出來的那一波
    /// 使用新間隔，已經在倒數中的那一波維持原樣（研究完成當下不會被提前）。
    ///
    /// Fortified reads <c>def.raidDelayRange</c> only when it (re)schedules a wave: the wave after one has fired,
    /// when a suspension ends, and when a map is first tracked. Swapping the def value on research completion and on
    /// load is therefore enough: the next wave scheduled uses the new interval while a wave already counting down
    /// keeps its time (completing the research never pulls it forward).
    /// </summary>
    public static class HuntIntervalUtility
    {
        public static ModExtension_HuntIntervalByResearch ExtensionOf(HuntdownDef def)
        {
            return def?.GetModExtension<ModExtension_HuntIntervalByResearch>();
        }

        /// <summary>
        /// 重新套用所有帶這個設定的追殺。讀檔、新遊戲與任一研究完成時呼叫；
        /// 只換 def 上的值，不動已經排好的計時。
        /// Re-applies the setting to every huntdown carrying it. Called on load, on a new game and whenever a research
        /// completes; it only swaps the def value and never touches a timer already scheduled.
        /// </summary>
        public static void RefreshAll()
        {
            if (Current.Game == null)
            {
                return;
            }
            List<HuntdownDef> defs;
            try
            {
                defs = DefDatabase<HuntdownDef>.AllDefsListForReading;
            }
            catch (Exception ex)
            {
                Log.Error($"[DMS] Hunt interval refresh could not read huntdown defs: {ex}");
                return;
            }
            if (defs == null)
            {
                return;
            }
            for (int i = 0; i < defs.Count; i++)
            {
                Apply(defs[i]);
            }
        }

        /// <summary>依研究狀態設定此追殺的間隔；沒掛設定或研究管理器尚未就緒時維持原值。</summary>
        public static void Apply(HuntdownDef def)
        {
            ModExtension_HuntIntervalByResearch ext = ExtensionOf(def);
            if (ext?.research == null)
            {
                return;
            }
            IntRange range = Finished(ext.research) ? ext.intervalAfter : ext.intervalBefore;
            if (def.raidDelayRange.min == range.min && def.raidDelayRange.max == range.max)
            {
                return;
            }
            def.raidDelayRange = range;
        }

        /// <summary>目前生效的間隔（除錯顯示用）。The interval in force, for debug output.</summary>
        public static string Describe(HuntdownDef def)
        {
            if (def == null)
            {
                return "<none>";
            }
            IntRange range = def.raidDelayRange;
            ModExtension_HuntIntervalByResearch ext = ExtensionOf(def);
            string gate = ext?.research == null
                ? "<no research gate>"
                : $"{(Finished(ext.research) ? "after" : "before")} {ext.research.defName}";
            return $"{range.min.ToStringTicksToPeriod()}~{range.max.ToStringTicksToPeriod()} ({gate})";
        }

        /// <summary>
        /// 研究是否已完成。研究管理器在 Def 載入期還不存在的專案上會拋例外（同 OccultechSanctionUtility），視為未完成。
        /// Whether the research is finished. Projects queried before the research manager exists throw (as in
        /// OccultechSanctionUtility), so treat that as not finished.
        /// </summary>
        private static bool Finished(ResearchProjectDef proj)
        {
            try
            {
                return proj.IsFinished;
            }
            catch
            {
                return false;
            }
        }
    }
}
