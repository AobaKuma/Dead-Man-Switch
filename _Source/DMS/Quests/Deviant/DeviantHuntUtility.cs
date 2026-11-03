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
        /// 追殺用的判定：脫序者是否真的不在殖民地手上。比 <see cref="IsLost"/> 寬：被逮捕成囚犯的，以及被流放但還沒走出
        /// 地圖的，都還能重新招募。追殺一結束就不會再開始，若這兩種也算結束，流放或逮捕後再招募就能留人又甩掉追殺。
        /// Whether the deviant is truly out of the colony's hands, for the hunt. Looser than <see cref="IsLost"/>: an arrested
        /// prisoner, or an exile who hasn't walked off the map, can still be recruited back. A stopped hunt never restarts,
        /// so ending it on either would let the player banish or arrest, re-recruit, and keep the deviant with no hunt.
        /// </summary>
        public static bool IsOutOfReach(Pawn pawn, out DeviantLostReason reason)
        {
            if (!IsLost(pawn, out reason))
            {
                return false;
            }
            if (reason == DeviantLostReason.Left && (pawn.IsPrisonerOfColony || (pawn.Faction == null && pawn.Spawned)))
            {
                reason = DeviantLostReason.None;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 目前受庇護、被艦隊追殺的脫序者；沒有時為 null。關係鎖與軍事法庭都看它。囚犯與尚未離開的流放者也算，
        /// 軍事法庭另外要求被告是自由殖民者。
        /// The sheltered deviant the fleet is hunting, or null. The relation lock and the court-martial both read this.
        /// Prisoners and exiles still on the map count; the court-martial separately requires a free colonist as defendant.
        /// </summary>
        public static Pawn HarboredDeviant
        {
            get
            {
                if (Current.Game == null) return null;
                HuntdownInstance instance = HuntdownUtility.GetInstance(DMS_DefOf.DMS_DeviantHunt);
                if (instance == null || !instance.hasBoundPawn) return null;
                return IsOutOfReach(instance.boundPawn, out _) ? null : instance.boundPawn;
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
