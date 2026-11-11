using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>
    /// 艦隊收押脫序者：交出引渡、或追殺期間在場外逮捕歸案，都走這裡。pawn 改屬艦隊、成為世界 pawn，並登記在
    /// <see cref="GameComponent_DeviantLegacy"/> 的收押名單：名單內的人不再是「自由」世界 pawn，原版不會拿他們去生成
    /// 襲擊或訪客，玩家也就不會在艦隊襲擊裡再見到、再俘虜回來。
    ///
    /// The fleet taking the deviant into custody, whether handed over on extradition or apprehended off-map during the
    /// hunt. The pawn joins the fleet as a world pawn and goes on the custody list in <see cref="GameComponent_DeviantLegacy"/>:
    /// listed pawns are no longer "free" world pawns, so vanilla never reuses them for raids or visitors and the player
    /// can't meet and capture them again.
    /// </summary>
    public static class DeviantCustodyUtility
    {
        /// <summary>
        /// 從綁架者、商隊、容器或地圖移出，解除床位，改屬艦隊並交給世界 pawn，登記收押。
        /// Out of the kidnapper's hands, the caravan, any container or the map; beds released; fleet faction; to world
        /// pawns; on the custody list.
        /// </summary>
        public static void TakeIntoCustody(Pawn pawn, Faction fleet)
        {
            if (pawn == null || pawn.Destroyed) return;

            List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < factions.Count; i++)
            {
                KidnappedPawnsTracker kidnapped = factions[i].kidnapped;
                if (kidnapped != null && kidnapped.KidnappedPawnsListForReading.Contains(pawn))
                {
                    kidnapped.RemoveKidnappedPawn(pawn);
                }
            }

            Caravan caravan = pawn.GetCaravan();
            if (caravan != null)
            {
                CaravanInventoryUtility.MoveAllInventoryToSomeoneElse(pawn, caravan.PawnsListForReading);
                caravan.RemovePawn(pawn);
            }
            if (pawn.Spawned)
            {
                pawn.DeSpawn();
            }
            else
            {
                pawn.holdingOwner?.Remove(pawn);
            }
            pawn.ownership?.UnclaimAll();
            if (fleet != null && pawn.Faction != fleet)
            {
                pawn.SetFaction(fleet);
            }
            // 收押者不再是任何人的囚犯或奴隸。No longer anyone's prisoner or slave.
            if (pawn.guest != null && (pawn.guest.IsPrisoner || pawn.guest.IsSlave))
            {
                pawn.guest.SetGuestStatus(null);
            }
            if (!pawn.IsWorldPawn())
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            GameComponent_DeviantLegacy.CompSafe?.AddToCustody(pawn);
        }

        public static bool InCustody(Pawn pawn) => GameComponent_DeviantLegacy.CompSafe?.InCustody(pawn) == true;
    }

    /// <summary>
    /// 收押中的脫序者不算「自由」世界 pawn：原版只從自由世界 pawn 裡挑人重用（襲擊、訪客、任務），也不會回收他們。
    /// A deviant in custody is not a free world pawn: vanilla only reuses free world pawns (raids, visitors, quests) and
    /// garbage-collects by the same situation.
    /// </summary>
    [HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.GetSituation))]
    public static class Patch_DeviantCustody_WorldPawnSituation
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn p, ref WorldPawnSituation __result)
        {
            if (__result == WorldPawnSituation.Free && DeviantCustodyUtility.InCustody(p))
            {
                __result = WorldPawnSituation.ReservedByQuest;
            }
        }
    }
}
