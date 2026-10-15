using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// Boss 死亡訊號。原版沒有「Boss 死了」的全域訊號（任務用的 "Killed" 只發給帶 questTags 的 pawn），
    /// 所以在 <see cref="Pawn.Kill"/> 後自己廣播：PawnKindDef.isBoss 的 pawn 死在地圖上時送出 <see cref="Tag"/>，
    /// 參數 SUBJECT = 死去的 Boss、POSITION = 死亡格、MAP = 死亡地圖、INSTIGATOR = 擊殺者（可能為 null）。
    /// 收訊端（例如 <see cref="CompBossKillRelay"/>）以 MAP 過濾，只理會同一張地圖的 Boss。
    ///
    /// The boss-killed signal. Vanilla has no global "a boss died" signal (the quest "Killed" signal only fires for
    /// pawns carrying questTags), so we broadcast one after <see cref="Pawn.Kill"/>: when a pawn whose
    /// PawnKindDef.isBoss is set dies on a map, send <see cref="Tag"/> with SUBJECT = the boss, POSITION = where it
    /// died, MAP = the map it died on and INSTIGATOR = the killer (may be null). Receivers such as
    /// <see cref="CompBossKillRelay"/> filter on MAP and only react to bosses on their own map.
    /// </summary>
    public static class BossKilledSignal
    {
        public const string Tag = "DMS_BossKilled";

        public static bool IsBoss(Pawn pawn) => pawn?.kindDef != null && pawn.kindDef.isBoss;

        public static void Send(Pawn boss, Map map, IntVec3 cell, DamageInfo? dinfo)
        {
            if (boss == null || map == null) return;
            Find.SignalManager.SendSignal(new Signal(Tag,
                boss.Named("SUBJECT"),
                cell.Named("POSITION"),
                map.Named("MAP"),
                (dinfo?.Instigator).Named("INSTIGATOR")));
        }
    }

    /// <summary>
    /// 死亡流程會把 pawn 移出地圖，Kill 結束後 MapHeld 要看屍體還在不在，所以先在 Prefix 記下地圖與位置。
    /// Kill despawns the pawn and afterwards MapHeld depends on whether a corpse survived, so capture the map and
    /// cell in the prefix.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_BossKilledSignal
    {
        public static void Prefix(Pawn __instance, out TargetInfo __state)
        {
            __state = !__instance.Dead && BossKilledSignal.IsBoss(__instance) && __instance.MapHeld != null
                ? new TargetInfo(__instance.PositionHeld, __instance.MapHeld)
                : TargetInfo.Invalid;
        }

        public static void Postfix(Pawn __instance, DamageInfo? dinfo, TargetInfo __state)
        {
            if (!__state.IsValid || !__instance.Dead) return;
            BossKilledSignal.Send(__instance, __state.Map, __state.Cell, dinfo);
        }
    }
}
