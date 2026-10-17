using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 次聲波塔：有電時每隔 intervalTicks 對範圍內的敵對生物放一次脈衝。
    /// 範圍內沒有敵人就不放、每 checkIdleTicks 再看一次，不白白對空氣發聲。
    /// Subsonic emitter: while powered, fires a pulse at hostiles in range every intervalTicks.
    /// With nobody in range it holds fire and re-checks every checkIdleTicks.
    /// </summary>
    public class CompProperties_SubsonicEmitter : CompProperties
    {
        public SubsonicPulseProps pulse = new SubsonicPulseProps();

        /// <summary>兩次脈衝的間隔。Interval between pulses.</summary>
        public IntRange intervalTicks = new IntRange(900, 1500);

        public int checkIdleTicks = 60;

        public CompProperties_SubsonicEmitter()
        {
            compClass = typeof(CompSubsonicEmitter);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (pulse == null || pulse.radius <= 0f)
                yield return $"{parentDef.defName}: CompProperties_SubsonicEmitter.pulse.radius must be > 0";
            if (intervalTicks.min <= 0)
                yield return $"{parentDef.defName}: CompProperties_SubsonicEmitter.intervalTicks must be > 0";
            if (parentDef.tickerType != TickerType.Normal)
                yield return $"{parentDef.defName}: CompSubsonicEmitter needs tickerType Normal";
        }
    }

    public class CompSubsonicEmitter : ThingComp
    {
        private int ticksToNextPulse = -1;

        private CompPowerTrader powerComp;

        public CompProperties_SubsonicEmitter Props => (CompProperties_SubsonicEmitter)props;

        private bool Active => powerComp == null || powerComp.PowerOn;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            if (ticksToNextPulse < 0)
                ticksToNextPulse = Props.intervalTicks.RandomInRange;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksToNextPulse, "ticksToNextPulse", -1);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !Active) return;

            if (ticksToNextPulse > 0)
            {
                ticksToNextPulse--;
                return;
            }

            if (!parent.IsHashIntervalTick(Props.checkIdleTicks)) return;
            if (!SubsonicUtility.AnyValidVictim(parent.Map, parent.Position, Props.pulse, parent)) return;

            SubsonicUtility.DoPulse(parent.Map, parent.Position, Props.pulse, parent);
            ticksToNextPulse = Props.intervalTicks.RandomInRange;
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            GenDraw.DrawRadiusRing(parent.Position, Props.pulse.radius);
        }

        public override string CompInspectStringExtra()
        {
            if (!Active) return null;
            if (ticksToNextPulse > 0)
                return "DMS_SubsonicEmitter_Charging".Translate(ticksToNextPulse.ToStringTicksToPeriod());
            return "DMS_SubsonicEmitter_Ready".Translate();
        }
    }
}
