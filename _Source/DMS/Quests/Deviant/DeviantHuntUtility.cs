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
        /// 脫序者是否真的不在殖民地手上；接納任務與追殺都用這個。比 <see cref="IsLost"/> 寬：被逮捕成囚犯的，以及被流放但
        /// 還沒走出地圖的，都還能重新招募。追殺一結束就不會再開始，若這兩種也算結束，流放或逮捕後再招募就能留人又甩掉追殺；
        /// 任務若算失敗，玩家只是把人關起來（例如打算交出）任務就被取消。
        /// Whether the deviant is truly out of the colony's hands, for both the refuge quest and the hunt. Looser than
        /// <see cref="IsLost"/>: an arrested prisoner, or an exile who hasn't walked off the map, can still be recruited back.
        /// A stopped hunt never restarts, so ending it on either would let the player banish or arrest, re-recruit, and keep
        /// the deviant with no hunt; failing the quest on them would cancel it just for locking the deviant up.
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
        /// 追殺唯一的結束條件（軍事法庭結案以外）：活要見人，死要收屍。死亡（之後若被復活，追殺會重新啟動，見
        /// <see cref="GameComponent_DeviantLegacy"/>）或 pawn 已被遊戲移除、永遠回不來。還活著只是不在殖民地手上的
        /// （放逐後走出地圖、被綁架、被別的派系帶走）不算結束，見 <see cref="IsAway"/>。
        /// The hunt's only end besides a concluded court-martial: seen alive or buried. Dead (a later resurrection restarts
        /// the hunt, see <see cref="GameComponent_DeviantLegacy"/>), or removed from the game for good. Alive but out of the
        /// colony's hands (banished off the map, kidnapped, taken by another faction) is not an end; see <see cref="IsAway"/>.
        /// </summary>
        public static bool IsGoneForGood(Pawn pawn)
        {
            return pawn == null || pawn.Dead || pawn.Destroyed || pawn.Discarded;
        }

        /// <summary>
        /// 脫序者還活著，但不在殖民地手上。追殺此時休眠（不發襲擊、不鎖關係），人一回到殖民地就恢復。
        /// The deviant is alive but out of the colony's hands. The hunt sleeps meanwhile (no raids, no relation lock) and
        /// resumes the moment they are back.
        /// </summary>
        public static bool IsAway(Pawn pawn)
        {
            return !IsGoneForGood(pawn) && IsOutOfReach(pawn, out _);
        }

        /// <summary>追殺綁定的脫序者，沒有追殺時為 null。The deviant the hunt is bound to, or null with no hunt.</summary>
        public static Pawn BoundDeviant
        {
            get
            {
                if (Current.Game == null) return null;
                HuntdownInstance instance = HuntdownUtility.GetInstance(DMS_DefOf.DMS_DeviantHunt);
                return instance != null && instance.hasBoundPawn ? instance.boundPawn : null;
            }
        }

        /// <summary>追殺進行中，但綁定的脫序者不在殖民地手上。The hunt is on but its deviant is away from the colony.</summary>
        public static bool BoundDeviantAway
        {
            get
            {
                Pawn pawn = BoundDeviant;
                return pawn != null && IsAway(pawn);
            }
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
                Pawn pawn = BoundDeviant;
                return pawn == null || IsOutOfReach(pawn, out _) ? null : pawn;
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
