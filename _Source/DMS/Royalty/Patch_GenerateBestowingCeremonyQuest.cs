using Verse;
using RimWorld;
using UnityEngine;
using HarmonyLib;
using RimWorld.QuestGen;

namespace DMS
{

    [HarmonyPatch(typeof(RoyalTitleUtility), nameof(RoyalTitleUtility.GenerateBestowingCeremonyQuest))]
    internal static class Patch_GenerateBestowingCeremonyQuest //確保生成的NPC具有正確的官銜陣營
    {
        public static bool Prefix(Pawn pawn, Faction faction)
        {
            if (pawn == null || pawn.Dead || faction == null)
            {
                return true;
            }

            if (faction.def == DMS_DefOf.DMS_Army)
            {
                // 原版只認得 QuestPart_BestowingCeremony,看不到受訓任務,每 37500 tick
                // 還是會再叫一次;這時受訓因「已有任務」跑不起來,就會退回典禮而兩份並存。
                // 所以任一種任務還在,就不再生成。
                if (QuestNode_Root_OfficerTraining.HasOngoingTraining(pawn)
                    || RoyalTitleUtility.GetCurrentBestowingCeremonyQuest(pawn, faction) != null)
                {
                    return false;
                }

                Slate slate = new Slate();
                slate.Set("titleHolder", pawn);
                slate.Set("bestowingFaction", faction);

                // 准尉與少校(掛有 TitleTrainingExtension 的階級)不辦典禮,改送去艦隊受訓。
                // 受訓任務跑不起來時(例如殖民地只剩一個人)自動退回典禮。
                QuestScriptDef script = DMS_DefOf.DMS_PromotionCeremony;
                RoyalTitleDef next = pawn.royalty?.GetTitleAwardedWhenUpdating(faction, pawn.royalty.GetFavor(faction));
                if (next?.GetModExtension<TitleTrainingExtension>() != null
                    && DMS_DefOf.DMS_OfficerTraining != null
                    && DMS_DefOf.DMS_OfficerTraining.CanRun(slate, pawn.MapHeld))
                {
                    script = DMS_DefOf.DMS_OfficerTraining;
                }

                if (script != null && (script == DMS_DefOf.DMS_OfficerTraining || script.CanRun(slate, pawn.MapHeld)))
                {
                    Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(script, slate);
                    if (quest.root.sendAvailableLetter)
                    {
                        QuestUtility.SendLetterQuestAvailable(quest);
                    }
                }
                return false;
            }
            else return true;
        }
    }

    [HarmonyPatch(typeof(RoyalTitleUtility), nameof(RoyalTitleUtility.EndExistingBestowingCeremonyQuest))]
    internal static class Patch_EndExistingBestowingCeremonyQuest //恩寵變動重生任務前,連同未接受的受訓任務一併作廢
    {
        public static void Postfix(Pawn pawn, Faction faction)
        {
            if (pawn == null || faction?.def != DMS_DefOf.DMS_Army) return;
            QuestNode_Root_OfficerTraining.EndPendingTraining(pawn);
        }
    }
}
