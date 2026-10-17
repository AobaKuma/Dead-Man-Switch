using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>脫序者為何不再受殖民地庇護。Why the deviant is no longer sheltered by the colony.</summary>
    public enum DeviantLostReason
    {
        None,
        /// <summary>死亡或消失。Dead or gone.</summary>
        Died,
        /// <summary>離開玩家派系：放逐、被逮捕、被招募走。Left the player faction: banished, arrested, recruited away.</summary>
        Left,
        Kidnapped,
    }

    /// <summary>
    /// 脫序者接納任務與追殺共用的判定。任務與 <see cref="HuntdownWorker_Deviant"/> 都輪詢這裡，
    /// 不依賴原版任務訊號（pawn 死亡時送出哪個訊號因路徑而異）。
    /// Checks shared by the deviant refuge quest and its huntdown. Both poll here instead of relying on vanilla quest
    /// signals, whose names on pawn death vary by code path.
    /// </summary>
    public static class DeviantHuntUtility
    {
        /// <summary>
        /// 脫序者是否已不在殖民地的庇護下。被軍事法庭借走的被告派系不變，不算失去。
        /// Whether the deviant is no longer sheltered by the colony. A defendant lent to a court-martial keeps their faction.
        /// </summary>
        public static bool IsLost(Pawn pawn, out DeviantLostReason reason)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Discarded)
            {
                reason = DeviantLostReason.Died;
                return true;
            }
            if (PawnUtility.IsKidnappedPawn(pawn))
            {
                reason = DeviantLostReason.Kidnapped;
                return true;
            }
            if (pawn.Faction != Faction.OfPlayer || pawn.IsPrisoner)
            {
                reason = DeviantLostReason.Left;
                return true;
            }
            reason = DeviantLostReason.None;
            return false;
        }

        /// <summary>
        /// 目前受庇護、被艦隊追殺的脫序者；沒有時為 null。關係鎖與軍事法庭都看它。
        /// The sheltered deviant the fleet is hunting, or null. The relation lock and the court-martial both read this.
        /// </summary>
        public static Pawn HarboredDeviant
        {
            get
            {
                if (Current.Game == null) return null;
                HuntdownInstance instance = HuntdownUtility.GetInstance(DMS_DefOf.DMS_DeviantHunt);
                if (instance == null || !instance.hasBoundPawn) return null;
                return IsLost(instance.boundPawn, out _) ? null : instance.boundPawn;
            }
        }

        /// <summary>
        /// 任務失敗信件（DMS_DeviantRefuge_Failed{suffix}Label/Text）。suffix：Died、Left、Kidnapped、NoArrival。
        /// The quest-failed letter (DMS_DeviantRefuge_Failed{suffix}Label/Text). suffix: Died, Left, Kidnapped, NoArrival.
        /// </summary>
        public static void SendFailedLetter(Pawn deviant, string suffix, Quest quest)
        {
            bool lookAt = suffix == "Left" && deviant != null && deviant.Spawned;
            Find.LetterStack.ReceiveLetter(
                ("DMS_DeviantRefuge_Failed" + suffix + "Label").Translate(deviant.Named("DEVIANT")),
                ("DMS_DeviantRefuge_Failed" + suffix + "Text").Translate(deviant.Named("DEVIANT")),
                LetterDefOf.NegativeEvent, lookAt ? new LookTargets(deviant) : null, null, quest);
        }

        public static string FailedSuffix(DeviantLostReason reason)
        {
            return reason == DeviantLostReason.Died ? "Died" : reason == DeviantLostReason.Kidnapped ? "Kidnapped" : "Left";
        }

        /// <summary>隱匿級追殺令是否進行中（含暫停中）。Whether the occultech kill order is running, suspended or not.</summary>
        public static bool KillOrderRunning
        {
            get
            {
                HuntdownDef def = GameComponent_OccultechSanction.HuntDef;
                return def != null && HuntdownUtility.IsActive(def);
            }
        }
    }
}
