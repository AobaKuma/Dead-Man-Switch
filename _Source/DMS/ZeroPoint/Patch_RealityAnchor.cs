using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 錨場內無法建立維度外的連接：靈能者不能施放靈能。玩家的技能按鈕顯示停用原因，AI 也一併擋下。
    /// Psycast 型別本身在 Assembly-CSharp 裡，沒有 Royalty 時只是永遠不會有人施放，patch 不需要 DLC 判斷。
    /// Inside a field no connection beyond this dimension can form, so psycasters cannot cast. The player's gizmo
    /// shows the reason and the AI is blocked too. Psycast lives in Assembly-CSharp; without Royalty nobody ever casts
    /// one, so the patch needs no DLC check.
    /// </summary>
    [HarmonyPatch(typeof(Psycast), nameof(Psycast.CanCast), MethodType.Getter)]
    public static class Patch_Psycast_CanCast_RealityAnchor
    {
        public static void Postfix(Psycast __instance, ref AcceptanceReport __result)
        {
            if (__result.Accepted && RealityAnchorUtility.IsAnchored(__instance.pawn))
                __result = "DMS_RealityAnchor_PsycastBlocked".Translate();
        }
    }

    [HarmonyPatch(typeof(Psycast), nameof(Psycast.GizmoDisabled))]
    public static class Patch_Psycast_GizmoDisabled_RealityAnchor
    {
        public static void Postfix(Psycast __instance, ref bool __result, ref string reason)
        {
            if (__result || !RealityAnchorUtility.IsAnchored(__instance.pawn)) return;
            __result = true;
            reason = "DMS_RealityAnchor_PsycastBlocked".Translate();
        }
    }

    /// <summary>
    /// 錨場內無法維持隱形（靈能隱形、獵影者、亡魂等）。比照原版的強制現形條件，接在私有屬性 ForcedVisible 上，
    /// 這樣現形特效、離開錨場後重新隱形的通知都沿用原版流程。
    /// Invisibility cannot hold inside a field (psychic invisibility, sightstealers, revenants and so on). This joins
    /// vanilla's own forced-visibility conditions on the private ForcedVisible getter, so the reveal effect and the
    /// return to invisibility after leaving the field both follow vanilla's flow.
    /// </summary>
    [HarmonyPatch(typeof(HediffComp_Invisibility), "ForcedVisible", MethodType.Getter)]
    public static class Patch_HediffComp_Invisibility_ForcedVisible_RealityAnchor
    {
        public static void Postfix(HediffComp_Invisibility __instance, ref bool __result)
        {
            if (!__result && RealityAnchorUtility.IsAnchored(__instance.Pawn))
                __result = true;
        }
    }
}
