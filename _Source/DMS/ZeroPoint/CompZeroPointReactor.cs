using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 零點能反應器：CompRefuelable 當成整塊燃料的彈匣，運轉時一次裝入一整塊、燒滿 ticksPerFuelUnit 才換下一塊。
    /// 燃料因此永遠是整數，原版補充邏輯（ceil 補滿）不會多塞一塊而白白浪費一年份的電力。
    /// 原版 CompPowerPlant 以 refuelable.HasFuel 判斷能否發電，但彈匣打空時爐心仍可能在燒，所以整段改寫。
    /// Zero-point reactor: the CompRefuelable is a magazine of whole fuel blocks. While running it loads one whole
    /// block at a time and burns it for ticksPerFuelUnit before loading the next. Fuel therefore stays a whole number,
    /// so vanilla's round-up refuelling never overfills and wastes a year's worth of power. Vanilla CompPowerPlant
    /// decides output from refuelable.HasFuel, but the core can still be burning after the magazine empties, so the
    /// output logic is replaced outright.
    /// </summary>
    public class CompProperties_ZeroPointReactor : CompProperties_Power
    {
        /// <summary>一塊燃料能燒多久。How long one fuel block burns.</summary>
        public int ticksPerFuelUnit = GenDate.TicksPerYear;

        public CompProperties_ZeroPointReactor()
        {
            compClass = typeof(CompZeroPointReactor);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (ticksPerFuelUnit <= 0)
                yield return $"{parentDef.defName}: CompProperties_ZeroPointReactor.ticksPerFuelUnit must be > 0";
            if (!parentDef.HasComp(typeof(CompRefuelable)))
                yield return $"{parentDef.defName}: CompZeroPointReactor needs a CompRefuelable for its fuel";
        }
    }

    public class CompZeroPointReactor : CompPowerPlant
    {
        /// <summary>爐心裡那一塊還能燒多久。Burn time left on the block in the core.</summary>
        private int coreTicksLeft;

        public CompProperties_ZeroPointReactor ReactorProps => (CompProperties_ZeroPointReactor)props;

        private int StoredUnits => refuelableComp == null ? 0 : (int)refuelableComp.Fuel;

        /// <summary>爐心加上彈匣，總共還能運轉多久。Core plus magazine, in ticks.</summary>
        public long TicksRemaining => coreTicksLeft + (long)StoredUnits * ReactorProps.ticksPerFuelUnit;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // 原版生成時先把輸出設成滿載，等第一個 tick 才修正；暫停中蓋好的空爐會先顯示 6000 W 灌進電網。
            // Vanilla starts a spawned plant at full output and only corrects it on the first tick, so an empty
            // reactor built while paused would show 6000 W on the grid until then.
            UpdateDesiredPowerOutput();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref coreTicksLeft, "zprCoreTicksLeft", 0);
        }

        public override void UpdateDesiredPowerOutput()
        {
            bool wantsToRun = (breakdownableComp == null || !breakdownableComp.BrokenDown)
                && (flickableComp == null || flickableComp.SwitchIsOn)
                && PowerOn;

            if (wantsToRun && coreTicksLeft <= 0 && refuelableComp != null && refuelableComp.Fuel >= 1f)
            {
                refuelableComp.ConsumeFuel(1f);
                coreTicksLeft = ReactorProps.ticksPerFuelUnit;
            }

            PowerOutput = wantsToRun && coreTicksLeft > 0 ? DesiredPowerOutput : 0f;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (PowerOutput > 0f && coreTicksLeft > 0)
                coreTicksLeft--;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            // 原版的缺燃料圖示只看彈匣，def 關掉它，改由這裡依爐心判斷。
            // Vanilla's out-of-fuel overlay only looks at the magazine; the def turns it off and this checks the core.
            if (TicksRemaining <= 0 && (refuelableComp == null || refuelableComp.allowAutoRefuel))
                parent.Map.overlayDrawer.DrawOverlay(parent, OverlayTypes.OutOfFuel);
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            string core = coreTicksLeft > 0
                ? "DMS_ZeroPointReactor_Core".Translate(coreTicksLeft.ToStringTicksToPeriod()).Resolve()
                : "DMS_ZeroPointReactor_CoreEmpty".Translate().Resolve();
            if (TicksRemaining > 0)
                core += "\n" + "DMS_ZeroPointReactor_Total".Translate(((int)System.Math.Min(TicksRemaining, int.MaxValue)).ToStringTicksToPeriod()).Resolve();
            return text.NullOrEmpty() ? core : text + "\n" + core;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;
            if (!DebugSettings.ShowDevGizmos) yield break;
            yield return new Command_Action
            {
                defaultLabel = "DEV: Burn out core",
                action = () => coreTicksLeft = 1
            };
        }
    }
}
