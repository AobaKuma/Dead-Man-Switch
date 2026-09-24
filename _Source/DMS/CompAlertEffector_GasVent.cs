using System.Collections.Generic;
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DMS
{
    public class CompProperties_AlertEffector_GasVent : CompProperties_AlertEffector
    {
        public GasType gasType = GasType.ToxGas;

        /// <summary>一次噴出的量，以「填滿幾格」計。Amount per release, in cells filled.</summary>
        public float cellsToFill = 24f;
        public float durationSeconds = 12f;

        /// <summary>噴氣前的預警時間。Warning time before gas comes out.</summary>
        public int warmupTicks = 120;

        /// <summary>一次釋放結束後的冷卻。Cooldown after a release ends.</summary>
        public int cooldownTicks = 600;

        public int maxCharges = 3;

        /// <summary>每回補一發所需 ticks；0 = 不回補。Ticks per recharge; 0 = never.</summary>
        public int rechargeTicks = 60000;

        public SoundDef warmupSound;
        public EffecterDef warmupEffecter;
        public EffecterDef releasingEffecter;

        public CompProperties_AlertEffector_GasVent()
        {
            compClass = typeof(CompAlertEffector_GasVent);
        }
    }

    /// <summary>
    /// 毒氣釋放口：收到設施警報後先預警，再從所在格噴出氣體。存量有限、會慢慢回補；
    /// 斷電、EMP、休眠會讓它停下，包括釋放到一半（判定用 FFF 的 AlertBuildingUtility.IsOperational）。
    /// 氣體不分敵我，但設施守軍是機兵，實際上只傷得到入侵者。
    ///
    /// Gas vent: on a facility alarm it warns, then releases gas from its cell. Limited charges that slowly
    /// refill; power loss, EMP or dormancy stop it, even mid-release (via FFF's AlertBuildingUtility.IsOperational).
    /// The gas is indiscriminate, but the garrison is mechanical, so in practice only intruders choke.
    /// </summary>
    public class CompAlertEffector_GasVent : CompAlertEffector
    {
        private const int ReleaseInterval = 30;

        private int warmupLeft = -1;
        private int remainingGas;
        private int cooldownUntil = -1;
        private int charges = -1;
        private int rechargeProgress;

        [Unsaved(false)]
        private Effecter effecter;

        public new CompProperties_AlertEffector_GasVent Props => (CompProperties_AlertEffector_GasVent)props;

        private int TotalGas => Mathf.CeilToInt(Props.cellsToFill * 255f);

        private int GasPerInterval => Mathf.Max(1, Mathf.RoundToInt(TotalGas / Mathf.Max(0.1f, Props.durationSeconds * 60f) * ReleaseInterval));

        private bool Warming => warmupLeft >= 0;

        private bool Releasing => remainingGas > 0;

        private bool Busy => Warming || Releasing;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (charges < 0) charges = Props.maxCharges;
        }

        protected override void DoEffect()
        {
            if (Busy || charges <= 0) return;
            if (Find.TickManager.TicksGame < cooldownUntil) return;

            charges--;
            warmupLeft = Props.warmupTicks;
            Props.warmupSound?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            SwapEffecter(Props.warmupEffecter);
            Messages.Message("DMS_GasVent_Warning".Translate(parent.Named("VENT")),
                parent, MessageTypeDefOf.ThreatSmall, historical: false);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned) return;

            TickRecharge();
            if (!Busy) return;

            // 斷電／EMP／休眠：立刻中止，這一發剩下的量作廢（存量在預警時就扣了）。
            // Unpowered / EMP'd / dormant: abort at once; the rest of this charge is lost (it was paid at warmup).
            if (!AlertBuildingUtility.IsOperational(parent))
            {
                Stop();
                return;
            }

            // effecter 不存檔，讀檔後在這裡重建。The effecter isn't saved; rebuild it after a load.
            if (effecter == null) SwapEffecter(Warming ? Props.warmupEffecter : Props.releasingEffecter);
            effecter?.EffectTick(parent, TargetInfo.Invalid);

            if (Warming)
            {
                if (--warmupLeft < 0)
                {
                    remainingGas = TotalGas;
                    SwapEffecter(Props.releasingEffecter);
                }
                return;
            }

            if (parent.IsHashIntervalTick(ReleaseInterval))
            {
                int amount = Mathf.Min(remainingGas, GasPerInterval);
                GasUtility.AddGas(parent.Position, parent.Map, Props.gasType, amount);
                remainingGas -= amount;
                if (Props.gasType == GasType.ToxGas)
                {
                    MapComponent_DMSToxGasFallback.Notify_GasReleased(parent.Map);
                }
                if (remainingGas <= 0) Stop();
            }
        }

        private void TickRecharge()
        {
            if (Props.rechargeTicks <= 0 || charges >= Props.maxCharges) return;
            if (++rechargeProgress >= Props.rechargeTicks)
            {
                rechargeProgress = 0;
                charges++;
            }
        }

        private void Stop()
        {
            warmupLeft = -1;
            remainingGas = 0;
            cooldownUntil = Find.TickManager.TicksGame + Props.cooldownTicks;
            SwapEffecter(null);
        }

        private void SwapEffecter(EffecterDef def)
        {
            effecter?.Cleanup();
            effecter = def != null && parent.Spawned ? def.Spawn(parent, parent.Map) : null;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            effecter?.Cleanup();
            effecter = null;
        }

        public override string CompInspectStringExtra()
        {
            string s = "DMS_GasVent_Charges".Translate(Mathf.Max(0, charges), Props.maxCharges);
            if (Warming)
            {
                s += "\n" + ((string)"DMS_GasVent_Arming".Translate()).Colorize(ColorLibrary.RedReadable);
            }
            else if (Releasing)
            {
                s += "\n" + ((string)"DMS_GasVent_Releasing".Translate()).Colorize(ColorLibrary.RedReadable);
            }
            return s;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Vent now",
                    action = delegate
                    {
                        cooldownUntil = -1;
                        if (charges <= 0) charges = 1;
                        DoEffect();
                    }
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref warmupLeft, "dms_warmupLeft", -1);
            Scribe_Values.Look(ref remainingGas, "dms_remainingGas", 0);
            Scribe_Values.Look(ref cooldownUntil, "dms_cooldownUntil", -1);
            Scribe_Values.Look(ref charges, "dms_charges", -1);
            Scribe_Values.Look(ref rechargeProgress, "dms_rechargeProgress", 0);
        }
    }

    /// <summary>
    /// 沒有 Biotech 時，原版毒氣不造成任何傷害（GasUtility 的毒素累積被 BiotechActive 閘住）。
    /// 毒氣釋放口噴過之後的一段時間內，用與原版相同的公式補上毒素累積。有 Biotech 時完全不動作，不會重複傷害。
    ///
    /// Without Biotech, vanilla tox gas does nothing (GasUtility gates the buildup behind BiotechActive). For a
    /// while after a gas vent fires, apply toxic buildup with vanilla's own formula. Does nothing at all with
    /// Biotech active, so there is never double damage.
    /// </summary>
    public class MapComponent_DMSToxGasFallback : MapComponent
    {
        private const int Interval = 50;
        private const int ActiveWindowTicks = 2 * GenDate.TicksPerDay;
        private const float ExtremeBuildupFactor = 0.25f;

        private int activeUntil = -1;

        public MapComponent_DMSToxGasFallback(Map map) : base(map) { }

        public static void Notify_GasReleased(Map map)
        {
            if (ModsConfig.BiotechActive || map == null) return;
            MapComponent_DMSToxGasFallback comp = map.GetComponent<MapComponent_DMSToxGasFallback>();
            if (comp != null) comp.activeUntil = Find.TickManager.TicksGame + ActiveWindowTicks;
        }

        public override void MapComponentTick()
        {
            if (activeUntil < 0 || ModsConfig.BiotechActive) return;
            int now = Find.TickManager.TicksGame;
            if (now > activeUntil)
            {
                activeUntil = -1;
                return;
            }
            if (now % Interval != 0) return;

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!Affected(pawn)) continue;
                byte density = pawn.Position.GasDensity(map, GasType.ToxGas);
                if (density == 0) continue;

                float f = density / 255f;
                Hediff buildup = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup);
                if (buildup != null && buildup.CurStageIndex == buildup.def.stages.Count - 1) f *= ExtremeBuildupFactor;
                ToxicUtility.DoPawnToxicDamage(pawn, f);
            }
        }

        private static bool Affected(Pawn pawn)
        {
            if (pawn.RaceProps.IsMechanoid || !pawn.RaceProps.IsFlesh) return false;
            if (pawn.RaceProps.Humanlike) return GasUtility.IsAffectedByExposure(pawn);   // 防毒面具等 / gas masks etc.
            return pawn.RaceProps.Animal;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activeUntil, "dms_toxFallbackUntil", -1);
        }
    }
}
