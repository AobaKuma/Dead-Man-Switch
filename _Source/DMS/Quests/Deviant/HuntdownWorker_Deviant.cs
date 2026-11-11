using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 脫序者追殺（DMS_DeviantHunt）的行為。排程、真空期與伴隨任務都由 Fortified Huntdown 處理。
    ///
    /// 隱匿級追殺令進行中、軍事法庭審理中、或脫序者還活著但不在殖民地手上時，這個追殺不追蹤任何地圖：計時直接移除，
    /// 事後重新排程。不用 CanFireWave 擋：那會讓地圖每輪重新排程並再發一次預警信件，玩家會一直收到不會兌現的預警。
    /// 活要見人，死要收屍：追殺只在脫序者死亡或永久消失時結束（<see cref="DeviantHuntUtility.IsGoneForGood"/>），
    /// 放逐、被綁架都只是休眠；死亡後若被復活，由 <see cref="GameComponent_DeviantLegacy"/> 重新啟動追殺。
    ///
    /// Behaviour of the deviant hunt (DMS_DeviantHunt). Scheduling, blackouts and quest offers come from Fortified Huntdown.
    /// While the occultech kill order runs, a court-martial is in session, or the deviant is alive but away from the
    /// colony, this hunt tracks no map: its timers are dropped and rescheduled afterwards. CanFireWave is not used for
    /// this because it reschedules the map every round and sends another warning letter each time, for raids that never
    /// come. Seen alive or buried: the hunt only ends when the deviant dies or is gone for good; banishment or a kidnapping
    /// only puts it to sleep, and a resurrection after death restarts it (<see cref="GameComponent_DeviantLegacy"/>).
    /// </summary>
    public class HuntdownWorker_Deviant : HuntdownWorker
    {
        // BoundPawnLost 與 OnStopped 在同一次檢查中先後呼叫；不存檔。Set by BoundPawnLost, consumed by OnStopped in the same check.
        private Pawn lostPawn;
        private DeviantLostReason lostReason;

        private static bool Dormant => DeviantHuntUtility.KillOrderRunning || OccultechSanctionUtility.CourtMartialOngoing
            || DeviantHuntUtility.BoundDeviantAway;

        public override bool CanTrackMap(Map map)
        {
            return base.CanTrackMap(map) && map.IsPlayerHome && !Dormant;
        }

        /// <summary>追殺的是收留脫序者的殖民地本身。The hunt targets the colony sheltering the deviant.</summary>
        public override bool StillHunted(Map map)
        {
            return base.StillHunted(map) && map.IsPlayerHome && !Dormant;
        }

        /// <summary>把這波襲擊登記為情境音樂的追殺遭遇。Registers the raid as a huntdown encounter for situational music.</summary>
        public override bool TryFireWave(Map map, HuntdownWave wave)
        {
            return DMSMusicEventBridge.FireAndRegisterHuntdown(map, Faction, () => base.TryFireWave(map, wave));
        }

        public override bool BoundPawnLost(Pawn pawn)
        {
            if (!DeviantHuntUtility.IsGoneForGood(pawn))
            {
                return false;
            }
            lostPawn = pawn;
            lostReason = pawn != null && pawn.Dead ? DeviantLostReason.Died : DeviantLostReason.Left;
            return true;
        }

        /// <summary>
        /// 只在脫序者失去時發信；軍事法庭結案由 QuestPart_CourtTruce 自己發訊息。無論哪種結束都整理一次傳票。
        /// Sends a letter only when the deviant was lost; a concluded court-martial posts its own message. Either way the
        /// pending summonses are refreshed.
        /// </summary>
        public override void OnStopped()
        {
            base.OnStopped();
            Pawn pawn = lostPawn;
            DeviantLostReason reason = lostReason;
            lostPawn = null;
            lostReason = DeviantLostReason.None;

            CourtMartialOfferUtility.RequestRefresh(ensureOffer: false);

            if (pawn == null)
            {
                return;
            }
            bool died = reason == DeviantLostReason.Died;
            if (died)
            {
                // 死要收屍：屍體還在就可能被復活，盯著它。Buried, not forgotten: a body can be revived, so keep watching it.
                GameComponent_DeviantLegacy.CompSafe?.WatchForRevival(pawn);
            }
            string fleet = Faction?.Name ?? "?";
            Find.LetterStack.ReceiveLetter(
                (died ? "DMS_DeviantHunt_EndedDiedLabel" : "DMS_DeviantHunt_EndedLeftLabel").Translate(),
                (died ? "DMS_DeviantHunt_EndedDiedText" : "DMS_DeviantHunt_EndedLeftText")
                    .Translate(pawn.Named("DEVIANT"), fleet.Named("FLEET")),
                // 被綁架或已離開地圖的 pawn 沒有可跳轉的位置。A kidnapped or departed pawn has nowhere to jump to.
                LetterDefOf.PositiveEvent, pawn.Spawned ? new LookTargets(pawn) : null, Faction);
        }

        /// <summary>
        /// 真空期結束。隱匿級追殺令也在進行時由它的 worker 發恢復信件，這裡不重複。
        /// Blackout over. When the kill order is running too, its worker sends the resumed letter; don't send a second one.
        /// </summary>
        public override void OnResumed(HuntdownInstance instance)
        {
            base.OnResumed(instance);
            if (DeviantHuntUtility.KillOrderRunning) return;
            OccultechSanctionUtility.SendHuntResumedLetter();
        }
    }
}
