using System.Collections.Generic;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    public class CompProperties_AlertEffector_SubsonicEmitter : CompProperties_AlertEffector
    {
        public SubsonicPulseProps pulse = new SubsonicPulseProps();

        /// <summary>作動期間兩次脈衝的間隔。Interval between pulses while engaged.</summary>
        public IntRange intervalTicks = new IntRange(600, 900);

        /// <summary>每次收到警報後維持作動的時間；再次警報會重新計時。How long one alarm keeps it engaged; a new alarm restarts the clock.</summary>
        public int activeTicks = 2500;

        /// <summary>從待機轉入作動到第一發脈衝的預熱。Warm-up from standby to the first pulse.</summary>
        public int firstPulseDelayTicks = 120;

        public int checkIdleTicks = 60;

        public CompProperties_AlertEffector_SubsonicEmitter()
        {
            compClass = typeof(CompAlertEffector_SubsonicEmitter);
            // 還在迷霧裡代表附近沒有入侵者，作動只是空轉計時。Still fogged means no intruders nearby; engaging would just run the clock.
            inactiveWhenFogged = true;
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (pulse == null || pulse.radius <= 0f)
                yield return $"{parentDef.defName}: CompProperties_AlertEffector_SubsonicEmitter.pulse.radius must be > 0";
            if (intervalTicks.min <= 0)
                yield return $"{parentDef.defName}: CompProperties_AlertEffector_SubsonicEmitter.intervalTicks must be > 0";
            if (activeTicks <= 0)
                yield return $"{parentDef.defName}: CompProperties_AlertEffector_SubsonicEmitter.activeTicks must be > 0";
            if (parentDef.tickerType != TickerType.Normal)
                yield return $"{parentDef.defName}: CompAlertEffector_SubsonicEmitter needs tickerType Normal";
        }
    }

    /// <summary>
    /// 設施版次聲波塔：平時待機不發聲；收到設施警報後作動 activeTicks，期間範圍內有敵對生物就間歇放脈衝。
    /// 斷電、EMP、休眠時不放（判定用 FFF 的 AlertBuildingUtility.IsOperational），但作動計時照走。
    /// 脈衝只打與設施陣營敵對的生物，且機械體免疫，所以守軍不受影響。
    ///
    /// Facility infrasonic emitter: silent on standby; a facility alarm engages it for activeTicks, during which
    /// it pulses whenever hostiles are in range. Unpowered, EMP'd or dormant it holds fire (via FFF's
    /// AlertBuildingUtility.IsOperational), though the engagement clock keeps running. Pulses only hit creatures
    /// hostile to the facility's faction, and mechs are immune, so the garrison is unaffected.
    /// </summary>
    public class CompAlertEffector_SubsonicEmitter : CompAlertEffector
    {
        private int activeUntil = -1;
        private int ticksToNextPulse = -1;

        private CompPowerTrader powerComp;
        private CompStunnable stunComp;
        private CompCanBeDormant dormantComp;

        public new CompProperties_AlertEffector_SubsonicEmitter Props => (CompProperties_AlertEffector_SubsonicEmitter)props;

        private bool Engaged => Find.TickManager.TicksGame < activeUntil;

        private bool Operational => AlertBuildingUtility.IsOperational(powerComp, stunComp, dormantComp);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            stunComp = parent.GetComp<CompStunnable>();
            dormantComp = parent.GetComp<CompCanBeDormant>();
        }

        protected override void DoEffect()
        {
            bool wasEngaged = Engaged;
            activeUntil = Find.TickManager.TicksGame + Props.activeTicks;
            if (wasEngaged) return;

            ticksToNextPulse = Props.firstPulseDelayTicks;
            Messages.Message("DMS_SubsonicEmitter_Engaged".Translate(parent.Named("EMITTER")),
                parent, MessageTypeDefOf.ThreatSmall, historical: false);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !Engaged || !Operational) return;

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
            if (!Engaged)
                return "DMS_SubsonicEmitter_Standby".Translate();

            string s = ((string)"DMS_SubsonicEmitter_EngagedFor".Translate(
                (activeUntil - Find.TickManager.TicksGame).ToStringTicksToPeriod())).Colorize(ColorLibrary.RedReadable);
            if (!Operational) return s;
            s += "\n" + (ticksToNextPulse > 0
                ? "DMS_SubsonicEmitter_Charging".Translate(ticksToNextPulse.ToStringTicksToPeriod())
                : "DMS_SubsonicEmitter_Ready".Translate());
            return s;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Engage now",
                    action = delegate
                    {
                        activeUntil = Find.TickManager.TicksGame + Props.activeTicks;
                        ticksToNextPulse = 0;
                    }
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref activeUntil, "dms_activeUntil", -1);
            Scribe_Values.Look(ref ticksToNextPulse, "dms_ticksToNextPulse", -1);
        }
    }
}
