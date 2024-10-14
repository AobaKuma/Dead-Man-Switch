using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DMS
{
    [HarmonyPatch(typeof(MechanitorUtility), "InMechanitorCommandRange")]
    internal class Patch_InMechanitorCommandRange
    {
        private static void Postfix(Pawn mech, LocalTargetInfo target, ref bool __result)
        {
            if (__result) return;

            if (mech.TryGetComp<CompCommandRelay>(out var _c))
            {
                __result = true;
                return;
            }
            List<Pawn> overseenPawns = MechanitorUtility.GetOverseer(mech)?.mechanitor?.OverseenPawns;
            foreach (Pawn item in overseenPawns.Where(p => p.Spawned))
            {
                if (item.TryGetComp<CompCommandRelay>(out var c) && item.Map == mech.Map && InRange(item, target, c.SquaredDistance))
                {
                    __result = true;
                    return;
                }
            }
            foreach (Pawn p in mech.Map.mapPawns.SpawnedPawnsInFaction(RimWorld.Faction.OfPlayer))
            {
                CompSubRelay comp = null;
                if (p.TryGetComp<CompSubRelay>(out comp) && InRange(p, target, comp.SquaredDistance))
                {
                    __result = true;
                    return;
                }
                if (p.apparel.WornApparel.Where(a => a.TryGetComp<CompSubRelay>(out comp)).Any() && InRange(p, target, comp.SquaredDistance))
                {
                    __result = true;
                    return;
                }
            }
        }
        private static bool InRange(Thing A, LocalTargetInfo B, float squaredRange)
        {
            if ((float)IntVec3Utility.DistanceToSquared(A.Position, B.Cell) <= squaredRange)
            {
                return true;
            }
            return false;
        }
    }
}
