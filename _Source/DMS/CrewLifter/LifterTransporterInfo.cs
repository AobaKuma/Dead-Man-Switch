using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 機動載人升降艙飛行途中的貨物資訊。
    /// 在原版 ActiveTransporterInfo 之上多帶著起飛時剩餘的燃料,落地後還給重建的升降艙。
    /// </summary>
    // 原版 ExposeData 非 virtual,重新實作 IExposable 才能讓 Scribe 呼叫到這裡的版本
    public class LifterTransporterInfo : ActiveTransporterInfo, IExposable
    {
        public float fuel;

        public LifterTransporterInfo() { }

        public LifterTransporterInfo(IThingHolder parent) : base(parent) { }

        public new void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fuel, "fuel");
        }
    }

    /// <summary>
    /// 掛在落地用 ActiveTransporter def 上:開艙時於原地建立哪個建築。
    /// </summary>
    public class LifterLandingExtension : DefModExtension
    {
        // 落地後在原地生成的建築 (完整升降艙 → 可再次發射;返回艙 → 廢棄艙體)
        public ThingDef spawnBuilding;
        // 生成的建築是否標記為「已脫離底座」(下次發射只發射返回艙)
        public bool markPodOnly;
    }
}
