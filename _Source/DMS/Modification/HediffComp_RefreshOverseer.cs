using Fortified;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在會改變指揮型機兵 MechBandwidth / MechControlGroups / FFF_MechCommandRange 的改裝件 hediff 上。
    /// FFF 的 CompOverseer 會快取這些數值，而且只在假人機械師的
    /// Pawn_MechanitorTracker.Notify_BandwidthChanged 觸發時才重算；
    /// 機兵本身的 hediff 增減並不會走到那條路徑，所以這裡在安裝與拆除時主動通知一次。
    /// </summary>
    public class HediffComp_RefreshOverseer : HediffComp
    {
        public HediffCompProperties_RefreshOverseer Props => (HediffCompProperties_RefreshOverseer)props;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            Notify();
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            Notify();
        }

        private void Notify()
        {
            // 改裝件也可能被安裝在非指揮型機兵上（例如 supportRaceDefs 留空時），此時什麼都不用做。
            if (Pawn is IOverseer overseer)
            {
                overseer.Comp?.Notify_BandwidthChanged();
            }
        }
    }

    public class HediffCompProperties_RefreshOverseer : HediffCompProperties
    {
        public HediffCompProperties_RefreshOverseer()
        {
            compClass = typeof(HediffComp_RefreshOverseer);
        }
    }
}
