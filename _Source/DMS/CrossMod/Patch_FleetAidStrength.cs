using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 艦隊的快速軍事支援(原版 RoyalTitlePermitWorker_CallAid 的許可)依外部橋接回報的據點實力縮放兵力。
    /// CallAid 只把 royalAid 的 pawnCount/points 抄進 IncidentParms 再交給 RaidFriendly,所以在 TryExecute
    /// 入口改 parms 即可,不必動共用的許可 def。沒有橋接時倍率為 1,不做任何事。
    /// Scales the fleet's quick military aid (vanilla RoyalTitlePermitWorker_CallAid permits) by the settlement
    /// strength an external bridge reports. CallAid only copies royalAid pawnCount/points into IncidentParms for
    /// RaidFriendly, so adjusting parms at TryExecute avoids touching the shared permit def. Without a bridge the factor
    /// is 1 and nothing happens.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class Patch_IncidentWorker_FleetAidStrength
    {
        [HarmonyPrefix]
        public static void Prefix(IncidentParms parms)
        {
            if (!FleetSettlementBridge.Active || parms == null || !parms.raidArrivalModeForQuickMilitaryAid) return;
            if (parms.faction?.def != DMS_DefOf.DMS_Army || !(parms.target is Map map)) return;

            float factor = FleetSettlementBridge.AidStrength(parms.faction, map);
            if (Mathf.Approximately(factor, 1f)) return;
            if (parms.pawnKind != null && parms.pawnCount > 0)
            {
                parms.pawnCount = Mathf.Max(1, Mathf.RoundToInt(parms.pawnCount * factor));
            }
            else if (parms.points > 0f)
            {
                parms.points *= factor;
            }
        }
    }
}
