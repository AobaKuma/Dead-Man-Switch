using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 穿上帶有 CompSurgicalApparel 的裝備時，自動套用原版的裝備鎖定。
    /// 鎖定後 Gear 面板的脫下鈕會停用、服裝最佳化 AI 不會替換、
    /// 活體無法被剝除，但屍體仍然會掉落（Pawn.Strip 對 Destroyed 傳入 dropLocked = true）。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear))]
    public static class Patch_ApparelTracker_Wear
    {
        [HarmonyPrefix]
        public static void Prefix(Apparel newApparel, ref bool locked)
        {
            if (!locked && newApparel.IsSurgicallyBonded())
            {
                locked = true;
            }
        }
    }

    /// <summary>
    /// 玩家在地圖上右鍵下令穿戴時，先跳出確認視窗說明「穿上後需要手術才能脫下」。
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuOptionProvider_Wear), "GetSingleOptionFor")]
    public static class Patch_FloatMenuOptionProvider_Wear
    {
        [HarmonyPostfix]
        public static void Postfix(Thing clickedThing, FloatMenuContext context, ref FloatMenuOption __result)
        {
            if (__result == null || __result.action == null)
            {
                return;
            }

            CompSurgicalApparel comp = clickedThing.GetSurgicalComp();
            if (comp == null || !comp.Props.confirmBeforeWearing)
            {
                return;
            }

            Pawn pawn = context?.FirstSelectedPawn;
            if (pawn == null)
            {
                return;
            }

            Action original = __result.action;
            TaggedString text = comp.Props.confirmMessageKey.Translate(
                clickedThing.LabelShortCap.ToString(),
                pawn.LabelShortCap.ToString(),
                comp.RemovalRecipeLabel);

            __result.action = delegate
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, original, destructive: true));
            };
        }
    }
}
