using Fortified;
using Verse;

namespace DMS
{
    /// <summary>
    /// 封存科技追殺令的行為。排程、暫停與伴隨任務都由 Fortified Huntdown 處理，這裡只補 DMS 特有的規則。
    /// Behaviour of the Occultech kill order. Scheduling, suspension and quest offers come from Fortified Huntdown;
    /// this only adds the DMS-specific rules.
    /// </summary>
    public class HuntdownWorker_Occultech : HuntdownWorker
    {
        /// <summary>
        /// 已接受的軍事法庭審理中是休戰：這一輪順延，審判失敗時追殺照常接續。
        /// An accepted court-martial is a truce: postpone this round; if the trial fails the hunt carries on.
        /// </summary>
        public override bool CanFireWave(Map map)
        {
            return base.CanFireWave(map) && !OccultechSanctionUtility.CourtMartialOngoing;
        }

        /// <summary>追殺的是殖民地本身：地圖不再是玩家據點時改追別處。The hunt targets the colony: drop maps that stop being home.</summary>
        public override bool StillHunted(Map map)
        {
            return base.StillHunted(map) && map.IsPlayerHome;
        }

        /// <summary>
        /// 暫停結束：SAGE 網路內另一台主機升格。補一張軍事法庭傳票（暫停期間沒有追殺，也就沒有人補發）。
        /// Suspension over: another SAGE host takes over. Re-offer the court-martial, which nothing did while paused.
        /// </summary>
        public override void OnResumed(HuntdownInstance instance)
        {
            base.OnResumed(instance);
            if (GameComponent_OccultechSanction.CompSafe?.PermanentHostile != true) return;
            OccultechSanctionUtility.SendHuntResumedLetter();
            OccultechSanctionUtility.EnsureCourtMartialOffered();
        }
    }
}
