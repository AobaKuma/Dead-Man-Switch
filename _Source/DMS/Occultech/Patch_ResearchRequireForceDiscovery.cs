using System;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 <see cref="ResearchProjectDef"/> 上的標記：此專案不會因為「前置研究全部完成」而自動探明，
    /// 只能由外力強制探明（資料匣、封存匣、脫序者移交等，皆走 <see cref="ResearchDiscoveryUtility.ForceDiscover"/>）。
    /// 用於沒有前置研究、但仍要在拿到檔案前保持隱藏的入口專案。
    /// </summary>
    public class ModExtension_RequireForceDiscovery : DefModExtension
    {
    }

    /// <summary>
    /// 接在 FFF 的探明唯一判定點上，而不是原生 IsHidden：FFF 的資料匣抽選（requireSealed）、
    /// 解鎖物遮罩、知識儲存都直接呼叫 IsUndiscovered，只改 IsHidden 會讓資料匣永遠抽不到這些專案。
    /// FFF 的 IsHidden patch 本身就是讀 IsUndiscovered，所以研究樹的隱藏表現一併生效。
    /// 只會把 false 收窄成 true；任何失敗一律維持原結果（fail-open）。
    /// </summary>
    [HarmonyPatch(typeof(ResearchDiscoveryUtility), nameof(ResearchDiscoveryUtility.IsUndiscovered))]
    public static class Patch_ResearchDiscoveryUtility_IsUndiscovered
    {
        private static bool errorLogged;

        [HarmonyPostfix]
        public static void Postfix(ResearchProjectDef proj, ref bool __result)
        {
            if (__result || proj == null || ResearchDiscoveryUtility.DebugRevealAll)
            {
                return;
            }
            try
            {
                if (!proj.HasModExtension<ModExtension_RequireForceDiscovery>() || !ResearchDiscoveryUtility.IsManaged(proj))
                {
                    return;
                }
                if (Current.Game == null || Find.ResearchManager == null)
                {
                    return;
                }
                if (proj.IsFinished || GameComponent_ResearchDiscovery.IsForceDiscoveredSafe(proj))
                {
                    return;
                }
                __result = true;
            }
            catch (Exception ex)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Log.Error($"[DMS] Force-discovery gating failed for {proj.defName}; left discovered: {ex}");
                }
            }
        }
    }
}
