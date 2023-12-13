﻿using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    [HarmonyPatch(typeof(MechRepairUtility), "IsMissingWeapon")]
    internal class Patch_IsMissingWeapon
    {
        public static void Postfix(ref bool __result, Pawn mech)
        {
            if (mech is WeaponUsable)
            {
                __result = false;
            }
        }
    }
}
