using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 HediffDef 上：帶有此 hediff 時，列出的想法只要心情值為負就歸零，正面的心情照常生效。
    /// 用於知能阻斷合劑，讓環境相關的負面想法（露宿、無桌進食、黑暗、寒冷、屍體、糟糕的房間等）不再扣心情。
    /// 原版 ThoughtDef.nullifyingHediffs 會連同正面階段一起抵銷（例如「令人印象深刻的臥室」），所以不採用。
    /// </summary>
    public class ModExtension_NullifyNegativeThoughts : DefModExtension
    {
        public List<ThoughtDef> thoughts = new List<ThoughtDef>();

        /// <summary>ThoughtDef → 會抵銷它負面心情的 hediff。第一次查詢時從 DefDatabase 建立。</summary>
        private static Dictionary<ThoughtDef, List<HediffDef>> cache;

        public static void Apply(Thought thought, ref float moodOffset)
        {
            if (moodOffset >= 0f)
            {
                return;
            }
            if (cache == null)
            {
                BuildCache();
            }
            if (!cache.TryGetValue(thought.def, out List<HediffDef> hediffs))
            {
                return;
            }
            HediffSet set = thought.pawn?.health?.hediffSet;
            if (set == null)
            {
                return;
            }
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (set.HasHediff(hediffs[i]))
                {
                    moodOffset = 0f;
                    return;
                }
            }
        }

        private static void BuildCache()
        {
            cache = new Dictionary<ThoughtDef, List<HediffDef>>();
            foreach (HediffDef hediffDef in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                ModExtension_NullifyNegativeThoughts ext = hediffDef.GetModExtension<ModExtension_NullifyNegativeThoughts>();
                if (ext == null)
                {
                    continue;
                }
                foreach (ThoughtDef thoughtDef in ext.thoughts)
                {
                    if (thoughtDef == null)
                    {
                        continue;
                    }
                    if (!cache.TryGetValue(thoughtDef, out List<HediffDef> list))
                    {
                        list = new List<HediffDef>();
                        cache[thoughtDef] = list;
                    }
                    list.Add(hediffDef);
                }
            }
        }
    }

    // Thought_Memory.MoodOffset 覆寫後會在 base 結果上再乘 moodPowerFactor、加 moodOffset，
    // 所以基底與記憶兩個版本都要攔。
    [HarmonyPatch(typeof(Thought), nameof(Thought.MoodOffset))]
    public static class Patch_Thought_MoodOffset
    {
        public static void Postfix(Thought __instance, ref float __result)
        {
            ModExtension_NullifyNegativeThoughts.Apply(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Thought_Memory), nameof(Thought_Memory.MoodOffset))]
    public static class Patch_Thought_Memory_MoodOffset
    {
        public static void Postfix(Thought_Memory __instance, ref float __result)
        {
            ModExtension_NullifyNegativeThoughts.Apply(__instance, ref __result);
        }
    }
}
