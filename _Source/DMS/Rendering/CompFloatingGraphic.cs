using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 讓 <see cref="CompFloatingGraphic"/> 知道建築現在是否在運作。建築上任何實作此介面的 comp 回傳 false，部件就落回底座。
    /// Tells <see cref="CompFloatingGraphic"/> whether the building is running. If any comp on the building that
    /// implements this returns false, the part settles back onto the base.
    /// </summary>
    public interface IFloatingGraphicDriver
    {
        bool FloatingGraphicActive { get; }
    }

    /// <summary>
    /// 在建築本體上方另畫一個懸浮部件：運作時升起 liftHeight 並以 bobPeriodTicks 為週期上下浮動，停止時落回原位。
    /// 「運作」= 有電（沒有 CompPowerTrader 時視為有電），且建築上所有 <see cref="IFloatingGraphicDriver"/> 都回傳 true。
    /// 只影響繪製，不存檔。需要 drawerType MapMeshAndRealTime 與 tickerType Normal。
    /// Draws a floating part above the building: while running it rises by liftHeight and drifts with a period of
    /// bobPeriodTicks, and it settles back when the building stops. Running means powered (a building without a
    /// CompPowerTrader counts as powered) and every <see cref="IFloatingGraphicDriver"/> on it returning true.
    /// Drawing only, nothing is saved. Needs drawerType MapMeshAndRealTime and tickerType Normal.
    /// </summary>
    public class CompProperties_FloatingGraphic : CompProperties
    {
        public GraphicData graphic;

        /// <summary>部件靜止時相對建築中心的位置。Part offset from the building centre at rest.</summary>
        public Vector3 offset = Vector3.zero;

        /// <summary>運作時升起的高度。How far the part rises while running.</summary>
        public float liftHeight = 0.2f;

        /// <summary>運作時的浮動幅度。Drift amplitude while running.</summary>
        public float bobAmplitude = 0.05f;

        public float bobPeriodTicks = 240f;

        /// <summary>升起與落下的速度（每 tick）。Rise and fall speed per tick.</summary>
        public float liftPerTick = 0.01f;

        public CompProperties_FloatingGraphic()
        {
            compClass = typeof(CompFloatingGraphic);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (graphic == null)
                yield return $"{parentDef.defName}: CompProperties_FloatingGraphic.graphic is null";
            if (bobPeriodTicks <= 0f || liftPerTick <= 0f)
                yield return $"{parentDef.defName}: CompProperties_FloatingGraphic.bobPeriodTicks and liftPerTick must be > 0";
            if (parentDef.drawerType != DrawerType.MapMeshAndRealTime)
                yield return $"{parentDef.defName}: CompFloatingGraphic needs drawerType MapMeshAndRealTime";
            if (parentDef.tickerType != TickerType.Normal)
                yield return $"{parentDef.defName}: CompFloatingGraphic needs tickerType Normal";
        }
    }

    public class CompFloatingGraphic : ThingComp
    {
        private CompPowerTrader power;
        private readonly List<IFloatingGraphicDriver> drivers = new List<IFloatingGraphicDriver>();

        /// <summary>目前升起的高度（只影響繪製，不存檔）。Current lift; drawing only, not saved.</summary>
        private float lift;

        public CompProperties_FloatingGraphic Props => (CompProperties_FloatingGraphic)props;

        private bool Active
        {
            get
            {
                if (!parent.Spawned || (power != null && !power.PowerOn)) return false;
                for (int i = 0; i < drivers.Count; i++)
                {
                    if (!drivers[i].FloatingGraphicActive) return false;
                }
                return true;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.GetComp<CompPowerTrader>();
            drivers.Clear();
            foreach (ThingComp comp in parent.AllComps)
            {
                if (comp is IFloatingGraphicDriver driver)
                    drivers.Add(driver);
            }
            lift = Active ? Props.liftHeight : 0f;
        }

        public override void CompTick()
        {
            base.CompTick();
            lift = Mathf.MoveTowards(lift, Active ? Props.liftHeight : 0f, Props.liftPerTick);
        }

        /// <summary>
        /// 部件此刻比靜止位置高出多少（升起加浮動）。給同建築上需要跟著部件移動的效果用，例如噴發口。
        /// How far the part currently sits above its rest position (lift plus drift), for effects on the same
        /// building that should follow the part, such as a spray nozzle.
        /// </summary>
        public float CurrentRise
        {
            get
            {
                if (Props.liftHeight <= 0f || lift <= 0f) return lift;
                float phase = (Find.TickManager.TicksGame + parent.thingIDNumber * 37) / Props.bobPeriodTicks * 2f * Mathf.PI;
                return lift + Mathf.Sin(phase) * Props.bobAmplitude * (lift / Props.liftHeight);
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (Props.graphic == null) return;

            Vector3 pos = parent.DrawPos + Props.offset;
            pos.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            pos.z += CurrentRise;
            Props.graphic.Graphic.Draw(pos, Rot4.North, parent);
        }
    }
}
