using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 把派系對玩家的關係「設定為」確切狀態，而不是「調整」。
    ///
    /// 原版 <see cref="Faction.TryAffectGoodwillWith"/> 不適合用來重置關係：
    /// 1. CalculateAdjustedGoodwillChange 會依自然好感度多加最多 25%，從 -100 重置到 0 實際會落在 +25 左右；
    /// 2. 正在攻擊對方據點、或有任務鎖住好感度時，CanChangeGoodwillFor 直接拒絕，好感度原封不動；
    /// 3. 有好感度的派系呼叫 SetRelationDirect 只會記錯誤、不會生效，所以關係類型其實只跟著門檻走。
    /// 軍事法庭休戰、宣判、封存科技制裁都是劇情上的「確定結果」，因此一律走這裡。
    ///
    /// Sets a faction's relation with the player to an exact state instead of nudging it.
    /// Vanilla TryAffectGoodwillWith is unsuitable for resets: it adds up to 25% toward natural goodwill
    /// (a reset from -100 to 0 lands near +25), CanChangeGoodwillFor refuses it outright while the player is
    /// attacking one of their settlements or a quest locks goodwill, and SetRelationDirect only logs an error for
    /// goodwill factions. Court-martial truces, verdicts and Occultech sanctions are definite outcomes, so they use this.
    /// </summary>
    public static class DMSRelationUtility
    {
        /// <summary>
        /// 設定確切好感度與關係類型（雙向），關係改變時照原版通知（囚犯、Lord、據點、信件）。
        /// 類型會修正成與好感度門檻一致，否則下一次 CheckKindThresholds 會把它翻回去。
        /// Sets exact goodwill and relation kind on both sides and runs vanilla's relation-change notifications.
        /// The kind is corrected to agree with the goodwill thresholds, or the next CheckKindThresholds would flip it.
        /// </summary>
        public static void SetGoodwillAndKind(Faction faction, int goodwill, FactionRelationKind kind,
            bool canSendLetter = false, HistoryEventDef reason = null)
        {
            Faction player = Faction.OfPlayer;
            if (faction == null || player == null || faction == player) return;

            if (!faction.HasGoodwill)
            {
                if (faction.RelationKindWith(player) != kind) faction.SetRelationDirect(player, kind, canSendLetter);
                return;
            }

            goodwill = Mathf.Clamp(goodwill, -100, 100);
            kind = KindAgreeingWith(goodwill, kind);

            FactionRelation mine = faction.RelationWith(player);
            FactionRelation theirs = player.RelationWith(faction);
            int change = goodwill - mine.baseGoodwill;
            if (reason != null && change != 0)
            {
                Find.HistoryEventsManager.RecordEvent(new HistoryEvent(reason,
                    faction.Named(HistoryEventArgsNames.AffectedFaction), change.Named(HistoryEventArgsNames.CustomGoodwill)));
            }

            FactionRelationKind previous = mine.kind;
            mine.baseGoodwill = goodwill;
            theirs.baseGoodwill = goodwill;
            mine.kind = kind;
            theirs.kind = kind;
            if (previous != kind)
            {
                faction.Notify_RelationKindChanged(player, previous, canSendLetter, null, GlobalTargetInfo.Invalid, out _);
                player.Notify_RelationKindChanged(faction, previous, false, null, GlobalTargetInfo.Invalid, out _);
            }
        }

        // 原版門檻（FactionRelation.CheckKindThresholds）：<= -75 敵對、>= 75 盟友；
        // 敵對要回到 >= 0 才轉中立，盟友要掉到 <= 0 才轉中立。
        // Vanilla thresholds: <= -75 hostile, >= 75 ally; hostile turns neutral at >= 0, ally at <= 0.
        private static FactionRelationKind KindAgreeingWith(int goodwill, FactionRelationKind kind)
        {
            if (goodwill <= -75) return FactionRelationKind.Hostile;
            if (goodwill >= 75) return FactionRelationKind.Ally;
            if (kind == FactionRelationKind.Hostile && goodwill >= 0) return FactionRelationKind.Neutral;
            if (kind == FactionRelationKind.Ally && goodwill <= 0) return FactionRelationKind.Neutral;
            return kind;
        }
    }
}
