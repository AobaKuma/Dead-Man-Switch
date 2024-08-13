using DMS;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DMS_Story
{
    [HarmonyPatch(typeof(FactionDialogMaker), nameof(FactionDialogMaker.FactionDialogFor))]
    internal static class Patch_FactionDialogMaker
    {
        public static bool Prefix(Pawn negotiator, Faction faction)
        {
            if (faction.def == DefOf.DMS_Army)
            {
                //已驗證是可以用。
                return true;
            }
            return true;
        }
    }
    public static class CommsConsole
    {
    }
}
