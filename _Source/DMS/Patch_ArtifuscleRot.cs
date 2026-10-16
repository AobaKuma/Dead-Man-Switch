using HarmonyLib;
using RimWorld;

namespace DMS
{
    /// <summary>
    /// 模組選項關閉「人工肌肉會腐爛」時，讓人工肌肉的 CompRottable 視為停用：
    /// 不再累積腐爛進度、不受腐爛傷害，也不顯示保鮮狀態。切換後立即生效，不必重開遊戲；
    /// 已經累積的腐爛進度會保留，重新開啟選項後從原處繼續。
    /// When the "Artifuscle rots" option is off, Artifuscle's CompRottable counts as inactive:
    /// no rot progress, no rot damage, no freshness readout. Takes effect immediately without a restart;
    /// existing rot progress is kept and resumes if the option is turned back on.
    /// </summary>
    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.Active), MethodType.Getter)]
    public static class Patch_CompRottable_Active
    {
        public static void Postfix(CompRottable __instance, ref bool __result)
        {
            if (__result && !DMS_Mod.Settings.artifuscleRots && __instance.parent.def == DMS_DefOf.Artifuscle)
            {
                __result = false;
            }
        }
    }
}
