using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace DMS
{
    /// <summary>
    /// 守墓人與分區閘門。
    /// 閘門是 FFF 的 <see cref="Building_RollingDoor_AccessLink"/>：控制台放行前誰都打不開，放行後永遠開著。
    /// 守墓人巡邏隊可以打開自己設施的分區閘門（不含獎勵房的密封門），門沒有放行，
    /// 所以照原版的門邏輯在最後一個人走過後自己關上。
    /// 迷霧裡的閘門被打開時不會揭霧（原版只有玩家方 pawn 進門才揭霧），連開關門的聲音也一併靜音，
    /// 玩家聽不到看不見的地方有東西在走動。
    ///
    /// Gravekeepers and the sector gates.
    /// The gates are FFF's <see cref="Building_RollingDoor_AccessLink"/>: nobody can open one until its console releases
    /// it, after which it stays open for good. A gravekeeper patrol may open its own facility's sector gates (never the
    /// treasury's sealed doors). The gate is not released, so the vanilla door logic closes it again once the last one
    /// is through. A gate opened inside the fog lifts no fog (vanilla only unfogs for player pawns entering a door), and
    /// its open/close sounds are muted too, so the player cannot hear movement in places they cannot see.
    /// </summary>
    public static class GravekeeperGateUtility
    {
        private static int silenceDepth;

        public static bool Silenced => silenceDepth > 0;

        public static bool CanPassGate(Building_RollingDoor_AccessLink gate, Pawn pawn)
        {
            // 先用勤務篩：LordOf 要掃過地圖上每個 lord，而尋路會頻繁問門能不能開。
            // Filter by duty first: LordOf scans every lord on the map, and pathfinding asks doors constantly.
            DutyDef duty = pawn.mindState?.duty?.def;
            if (duty == null
                || (duty != DMS_DefOf.DMS_GravekeeperPatrol && duty != DMS_DefOf.DMS_GravekeeperChallenge && duty != DMS_DefOf.DMS_GravekeeperEngage))
            {
                return false;
            }
            return pawn.GetLord()?.LordJob is LordJob_GravekeeperPatrol patrol && patrol.CanOpenGate(gate);
        }

        /// <summary>還沒放行、又在迷霧裡的密封門：開始靜音並回傳 true。An unreleased sealed door in the fog: mute and return true.</summary>
        public static bool BeginSilenceIfFogged(Building_Door door)
        {
            if (!(door is Building_RollingDoor_AccessLink gate) || gate.activated || !door.Spawned) return false;
            if (!door.Position.Fogged(door.Map)) return false;
            silenceDepth++;
            return true;
        }

        public static void EndSilence()
        {
            if (silenceDepth > 0) silenceDepth--;
        }
    }

    [HarmonyPatch(typeof(Building_RollingDoor_AccessLink), nameof(Building_RollingDoor_AccessLink.PawnCanOpen))]
    public static class Patch_AccessLinkDoor_GravekeeperPass
    {
        public static void Postfix(Building_RollingDoor_AccessLink __instance, Pawn p, ref bool __result)
        {
            if (__result || __instance.activated || p == null) return;
            if (GravekeeperGateUtility.CanPassGate(__instance, p))
            {
                __result = p.CanOpenDoors;
            }
        }
    }

    [HarmonyPatch(typeof(Building_Door), "DoorOpen")]
    public static class Patch_Door_DoorOpen_FogSilence
    {
        public static void Prefix(Building_Door __instance, out bool __state)
        {
            __state = GravekeeperGateUtility.BeginSilenceIfFogged(__instance);
        }

        // Finalizer：開門途中拋例外也要解除靜音，否則之後全遊戲都沒聲音。
        // Finalizer: unmute even if opening throws, or every sound in the game would stay silent.
        public static void Finalizer(bool __state)
        {
            if (__state) GravekeeperGateUtility.EndSilence();
        }
    }

    [HarmonyPatch(typeof(Building_Door), "DoorTryClose")]
    public static class Patch_Door_DoorTryClose_FogSilence
    {
        public static void Prefix(Building_Door __instance, out bool __state)
        {
            __state = GravekeeperGateUtility.BeginSilenceIfFogged(__instance);
        }

        public static void Finalizer(bool __state)
        {
            if (__state) GravekeeperGateUtility.EndSilence();
        }
    }

    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot))]
    public static class Patch_SoundStarter_PlayOneShot_FogSilence
    {
        public static bool Prefix()
        {
            return !GravekeeperGateUtility.Silenced;
        }
    }
}
