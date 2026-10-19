using HarmonyLib;
using RimWorld;

namespace DMS
{
    /// <summary>
    /// Renegade Clan periodically penalizes fleet goodwill for an alliance with the renegades.
    /// Suspend that automatic penalty during a court-martial; actual player provocations still break the truce.
    /// 叛軍同盟的定期扣分不得打破軍事法庭休戰；玩家挑釁造成的扣分仍照常生效。
    /// Match the external history def by name so Renegade Clan stays an optional dependency.
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class Patch_CourtTruce_RenegadeGoodwill
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, Faction other, int goodwillChange,
            HistoryEventDef reason, ref bool __result)
        {
            if (goodwillChange >= 0 || reason?.defName != "DMSRC_AllyWithRenegades"
                || !OccultechRelationGuard.IsPlayerFleetPair(__instance, other, out _)
                || !OccultechSanctionUtility.CourtMartialOngoing)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
