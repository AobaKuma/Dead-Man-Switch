using Verse;
using HarmonyLib;
using RimWorld;

namespace DMS
{
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(CallBossgroupUtility), "BossgroupEverCallable")]
    public static class MechanitorPatch
    {
        public static bool Prefix(BossgroupDef def, ref AcceptanceReport __result)
        {
            if (def.HasModExtension<NoMechanitorNeed>())
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}