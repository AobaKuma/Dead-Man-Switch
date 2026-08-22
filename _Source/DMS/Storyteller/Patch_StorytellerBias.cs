using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    public static class StorytellerBiasUtility
    {
        /// <summary>
        /// 目前這局說書人身上的加權設定，沒有就回傳 null。
        /// The bias extension on the storyteller of the current game, or null.
        /// </summary>
        public static ModExtension_StorytellerBias CurrentBias
        {
            get
            {
                Storyteller storyteller = Current.Game?.storyteller;
                if (storyteller == null || storyteller.def == null)
                {
                    return null;
                }
                return storyteller.def.GetModExtension<ModExtension_StorytellerBias>();
            }
        }
    }

    /// <summary>
    /// 隨機任務池：把來自指定模組的 QuestScriptDef 挑選權重乘上倍率。
    /// Random quest pool: multiply the selection weight of quests from the
    /// favoured mods.
    /// </summary>
    [HarmonyPatch(typeof(NaturalRandomQuestChooser), nameof(NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight))]
    public static class Patch_NaturalRandomQuestChooser_GetNaturalRandomSelectionWeight
    {
        public static void Postfix(QuestScriptDef quest, ref float __result)
        {
            // 權重 0 代表這個任務現在不能發（點數不足、冷卻中……），不要把它救回來。
            // A weight of 0 means the quest is not eligible right now; leave it out.
            if (__result <= 0f)
            {
                return;
            }
            ModExtension_StorytellerBias bias = StorytellerBiasUtility.CurrentBias;
            if (bias == null || bias.questSelectionWeightFactor == 1f)
            {
                return;
            }
            if (!bias.AppliesTo(quest))
            {
                return;
            }
            __result *= bias.questSelectionWeightFactor;
        }
    }

    /// <summary>
    /// 隨機事件池：把來自指定模組的 IncidentDef 最終挑選權重乘上倍率。
    /// Random incident pool: multiply the final selection weight of incidents
    /// from the favoured mods.
    /// </summary>
    [HarmonyPatch(typeof(StorytellerComp), "IncidentChanceFinal")]
    public static class Patch_StorytellerComp_IncidentChanceFinal
    {
        public static void Postfix(IncidentDef def, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }
            ModExtension_StorytellerBias bias = StorytellerBiasUtility.CurrentBias;
            if (bias == null || bias.incidentChanceFactor == 1f)
            {
                return;
            }
            if (!bias.AppliesTo(def))
            {
                return;
            }
            __result *= bias.incidentChanceFactor;
        }
    }

    /// <summary>
    /// 突襲派系：RaidCommonalityFromPoints 只在挑選突襲來源派系時被呼叫，
    /// 所以在這裡加權不會影響世界生成或其他派系邏輯。
    /// Raid factions: RaidCommonalityFromPoints is only used when picking the
    /// faction behind a raid, so weighting here does not touch world
    /// generation or any other faction logic.
    /// </summary>
    [HarmonyPatch(typeof(FactionDef), nameof(FactionDef.RaidCommonalityFromPoints))]
    public static class Patch_FactionDef_RaidCommonalityFromPoints
    {
        public static void Postfix(FactionDef __instance, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }
            ModExtension_StorytellerBias bias = StorytellerBiasUtility.CurrentBias;
            if (bias == null || bias.raidCommonalityFactor == 1f)
            {
                return;
            }
            if (!bias.Favors(__instance))
            {
                return;
            }
            __result *= bias.raidCommonalityFactor;
        }
    }
}
