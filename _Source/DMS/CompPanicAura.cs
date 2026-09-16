using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// ThingComp that emits a fear aura: every <see cref="CompProperties_PanicAura.intervalTicks"/> ticks,
    /// each hostile humanlike pawn within <see cref="CompProperties_PanicAura.radius"/> cells has a
    /// <see cref="CompProperties_PanicAura.panicChance"/> probability of being forced into the
    /// PanicFlee MentalState. Pawns whose <see cref="Pawn.BodySize"/> exceeds
    /// <see cref="CompProperties_PanicAura.maxAffectedBodySize"/> are immune (e.g. heavy mechs).
    /// </summary>
    public class CompPanicAura : ThingComp
    {
        private int ticksUntilNextPulse;

        public CompProperties_PanicAura Props => (CompProperties_PanicAura)props;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
                ticksUntilNextPulse = Props.intervalTicks;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilNextPulse, "ticksUntilNextPulse", 0);
        }

        // ── Tick ───────────────────────────────────────────────────────────────

        public override void CompTick()
        {
            base.CompTick();

            if (!parent.Spawned)
                return;

            ticksUntilNextPulse--;
            if (ticksUntilNextPulse > 0)
                return;

            ticksUntilNextPulse = Props.intervalTicks;
            DoPanicPulse();
        }

        // ── Core logic ─────────────────────────────────────────────────────────

        private void DoPanicPulse()
        {
            Map map = parent.Map;
            if (map == null)
                return;

            // AllHumanlikeSpawned is the correct collection for area-of-effect operations
            // targeting humanlike pawns (see Building_FrenzyInducer, Building_SleepSuppressor).
            // Using a for loop by index is the RimWorld convention to avoid iterator issues
            // if TryStartMentalState triggers side effects.
            List<Pawn> pawns = map.mapPawns.AllHumanlikeSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];

                if (!IsValidTarget(pawn))
                    continue;

                if (!pawn.Position.InHorDistOf(parent.Position, Props.radius))
                    continue;

                if (!Rand.Chance(Props.panicChance))
                    continue;

                pawn.mindState.mentalStateHandler.TryStartMentalState(
                    MentalStateDefOf.PanicFlee,
                    reason: null,
                    forced: false,
                    forceWake: false,
                    causedByMood: false,
                    otherPawn: null,
                    transitionSilently: true
                );
            }
        }

        // AllHumanlikeSpawned already guarantees: Spawned + Humanlike.
        // We additionally require: alive, not downed, awake, hostile, not already panicking,
        // and BodySize within the affected range (large targets are immune).
        private bool IsValidTarget(Pawn pawn)
        {
            return !pawn.Dead
                && !pawn.Downed
                && pawn.Awake()
                && pawn.HostileTo(parent)
                && pawn.BodySize <= Props.maxAffectedBodySize
                && pawn.mindState?.mentalStateHandler?.CurStateDef != MentalStateDefOf.PanicFlee;
        }

        // ── Inspect string ─────────────────────────────────────────────────────

        public override string CompInspectStringExtra()
        {
            if (!Props.showInspectString)
                return null;
            return "DMS_PanicAura_NextPulseIn".Translate(ticksUntilNextPulse.ToStringTicksToPeriod());
        }
    }

    // ── CompProperties ─────────────────────────────────────────────────────────

    public class CompProperties_PanicAura : CompProperties
    {
        /// <summary>Radius in cells within which enemies may panic.</summary>
        public float radius = 5f;

        /// <summary>Ticks between each aura pulse. Default 300 (5 seconds).</summary>
        public int intervalTicks = 300;

        /// <summary>Probability [0..1] per enemy pawn per pulse to trigger PanicFlee.</summary>
        public float panicChance = 0.15f;

        /// <summary>
        /// Maximum BodySize a pawn may have to be affected by the aura.
        /// Pawns with BodySize > this value are immune (treated as too large/imposing to panic).
        /// Pawn.BodySize = CurLifeStage.bodySizeFactor × RaceProps.baseBodySize.
        /// Reference values: human ≈ 1.0, muffalo ≈ 2.0, heavy mechs vary.
        /// Default is float.MaxValue (no exemption — all body sizes are affected).
        /// </summary>
        public float maxAffectedBodySize = float.MaxValue;

        /// <summary>Whether to show a countdown line in the inspect panel.</summary>
        public bool showInspectString = false;

        public CompProperties_PanicAura()
        {
            compClass = typeof(CompPanicAura);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string err in base.ConfigErrors(parentDef))
                yield return err;

            if (radius <= 0f)
                yield return $"{parentDef.defName}: CompProperties_PanicAura.radius must be > 0";

            if (intervalTicks <= 0)
                yield return $"{parentDef.defName}: CompProperties_PanicAura.intervalTicks must be > 0";

            if (panicChance < 0f || panicChance > 1f)
                yield return $"{parentDef.defName}: CompProperties_PanicAura.panicChance must be in [0, 1]";

            if (maxAffectedBodySize <= 0f)
                yield return $"{parentDef.defName}: CompProperties_PanicAura.maxAffectedBodySize must be > 0";
        }
    }
}
