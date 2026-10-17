using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 交付前的監控（接受時啟用，交付時停用）。每 250 tick 輪詢：
    /// - 尚未加入：逾時（找不到可進場的地圖）→ 失敗。
    /// - 已加入：脫序者死亡、離開派系或被綁架 → 依原因發信，失敗。
    /// 用輪詢而不是原版任務訊號：pawn 死亡時送出的訊號依路徑而異，輪詢與追殺用的是同一個判定。
    ///
    /// Watch before the handover (enabled on accept, disabled at the handover), polled every 250 ticks: not joined in
    /// time → fail; joined and then dead, gone from the faction or kidnapped → letter by reason, fail. Polling rather
    /// than vanilla signals, whose names on pawn death vary; the same check drives the huntdown.
    /// </summary>
    public class QuestPart_DeviantWatch : QuestPartActivable
    {
        public Pawn deviant;
        public int arrivalTimeoutTicks = 60000;
        public string outSignalFailed;

        private bool joined;
        private bool resolved;

        private const int CheckIntervalTicks = 250;

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (resolved || Find.TickManager.TicksGame % CheckIntervalTicks != 0) return;

            if (!joined)
            {
                // 加入前脫序者屬於艦隊，不能套用 IsLost。Before joining the deviant belongs to the fleet; IsLost doesn't apply.
                if (deviant != null && !deviant.Dead && deviant.Faction == Faction.OfPlayer)
                {
                    joined = true;
                }
                else if (Find.TickManager.TicksGame - EnableTick >= arrivalTimeoutTicks)
                {
                    Fail("NoArrival");
                }
                return;
            }

            if (DeviantHuntUtility.IsLost(deviant, out DeviantLostReason reason))
            {
                Fail(DeviantHuntUtility.FailedSuffix(reason));
            }
        }

        private void Fail(string suffix)
        {
            resolved = true;
            DeviantHuntUtility.SendFailedLetter(deviant, suffix, quest);
            Find.SignalManager.SendSignal(new Signal(outSignalFailed));
            Complete();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref deviant, "deviant");
            Scribe_Values.Look(ref arrivalTimeoutTicks, "arrivalTimeoutTicks", 60000);
            Scribe_Values.Look(ref outSignalFailed, "outSignalFailed");
            Scribe_Values.Look(ref joined, "joined");
            Scribe_Values.Look(ref resolved, "resolved");
        }
    }
}
