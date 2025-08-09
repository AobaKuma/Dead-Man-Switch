using Verse;
using HarmonyLib;

namespace DMS
{
    [StaticConstructorOnStartup]
    public static class HarmonyEntry
    {
        static HarmonyEntry()
        {
            Harmony entry = new Harmony("AOBA.DMS");
            entry.PatchAll();
        }
    }
}