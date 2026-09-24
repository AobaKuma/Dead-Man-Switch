using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DMS
{
    /// <summary>
    /// 爆破探勘裝置：裝填一發迫擊砲彈後點火，引信燒完即引爆，
    /// 以震波回波一次探明數處深層礦脈，但有高機率驚醒地下休眠的蟲群。
    /// 砲彈由同物件上的 CompRefuelable 管理（容量 1、不隨時間消耗）。
    /// Blast scanner: load one mortar shell and light the fuse; when it burns down the charge fires,
    /// the returning shockwave maps several deep ore lumps at once, and there is a high chance of
    /// waking dormant insects underground. The shell is held by a CompRefuelable on the same thing
    /// (capacity 1, no passive consumption).
    /// </summary>
    public class CompProperties_BlastScanner : CompProperties
    {
        public int fuseTicks = 240;

        /// <summary>引爆後的冷卻。Cooldown after a detonation.</summary>
        public int cooldownTicks = 120000;

        /// <summary>一次探明的礦脈數。Deep lumps found per detonation.</summary>
        public IntRange lumpCount = new IntRange(2, 3);

        public float infestationChance = 0.8f;

        /// <summary>蟲巢從裝置周圍這個距離內鑽出。Hives break out within this distance of the scanner.</summary>
        public FloatRange infestationDistance = new FloatRange(6f, 16f);

        /// <summary>蟲群規模 = 當下威脅點數 × 此值，換算成巢數（每巢 220 點，同原版）。Swarm size = threat points × this, 220 pts per hive like vanilla.</summary>
        public float infestationPointsFactor = 0.6f;

        public IntRange hiveCountLimits = new IntRange(1, 4);

        public SoundDef igniteSound;
        public SoundDef detonateSound;

        public CompProperties_BlastScanner()
        {
            compClass = typeof(CompBlastScanner);
        }
    }

    public class CompBlastScanner : ThingComp
    {
        private int fuseTicksLeft = -1;
        private int cooldownUntilTick = -1;

        private CompRefuelable refuelable;

        public CompProperties_BlastScanner Props => (CompProperties_BlastScanner)props;

        private bool FuseLit => fuseTicksLeft >= 0;

        private int CooldownTicksLeft => Mathf.Max(0, cooldownUntilTick - Find.TickManager.TicksGame);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            refuelable = parent.GetComp<CompRefuelable>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref fuseTicksLeft, "fuseTicksLeft", -1);
            Scribe_Values.Look(ref cooldownUntilTick, "cooldownUntilTick", -1);
        }

        public AcceptanceReport CanIgnite
        {
            get
            {
                if (FuseLit) return "DMS_BlastScanner_FuseLit".Translate();
                if (!parent.Map.Biome.hasBedrock) return "CannotUseScannerNoBedrock".Translate();
                if (CooldownTicksLeft > 0) return "DMS_BlastScanner_Cooldown".Translate(CooldownTicksLeft.ToStringTicksToPeriod());
                if (refuelable != null && refuelable.Fuel < 1f) return "DMS_BlastScanner_NoCharge".Translate();
                return true;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            if (parent.Faction != Faction.OfPlayer) yield break;

            AcceptanceReport report = CanIgnite;
            Command_Action ignite = new Command_Action
            {
                defaultLabel = "DMS_BlastScanner_Ignite".Translate(),
                defaultDesc = "DMS_BlastScanner_IgniteDesc".Translate(),
                icon = ChargeIcon(),
                action = Ignite,
            };
            if (!report.Accepted)
                ignite.Disable(report.Reason);
            yield return ignite;

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Detonate now",
                    action = Detonate,
                };
                if (CooldownTicksLeft > 0)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "DEV: Reset cooldown",
                        action = () => cooldownUntilTick = -1,
                    };
                }
            }
        }

        private Texture2D ChargeIcon()
        {
            ThingDef shell = refuelable?.Props.fuelFilter?.AnyAllowedDef;
            return shell?.uiIcon ?? BaseContent.BadTex;
        }

        private void Ignite()
        {
            fuseTicksLeft = Props.fuseTicks;
            Props.igniteSound?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!FuseLit || !parent.Spawned) return;

            if (parent.IsHashIntervalTick(15))
                FleckMaker.ThrowMicroSparks(parent.DrawPos, parent.Map);

            // 引信燒到一半砲彈被卸掉（拆除、被偷、遭破壞）就熄火。
            // If the shell is gone mid-fuse (uninstalled, stolen, destroyed), the fuse goes out.
            if (refuelable != null && refuelable.Fuel < 1f)
            {
                fuseTicksLeft = -1;
                return;
            }

            fuseTicksLeft--;
            if (fuseTicksLeft < 0)
                Detonate();
        }

        private void Detonate()
        {
            fuseTicksLeft = -1;
            Map map = parent.Map;
            if (map == null) return;

            if (refuelable != null && refuelable.Fuel >= 1f)
                refuelable.ConsumeFuel(1f);
            cooldownUntilTick = Find.TickManager.TicksGame + Props.cooldownTicks;

            PlayBlastEffects(map);

            List<ThingDef> found = new List<ThingDef>();
            IntVec3 firstLump = IntVec3.Invalid;
            if (map.Biome.hasBedrock)
            {
                int count = Props.lumpCount.RandomInRange;
                for (int i = 0; i < count; i++)
                {
                    if (TryGenerateLump(map, out ThingDef lumpDef, out IntVec3 at))
                    {
                        found.Add(lumpDef);
                        if (!firstLump.IsValid) firstLump = at;
                    }
                }
            }

            if (found.Count > 0)
            {
                string list = found.GroupBy(d => d)
                    .Select(g => g.Count() > 1 ? $"{g.Key.LabelCap} x{g.Count()}" : g.Key.LabelCap.ToString())
                    .ToLineList("  - ");
                Find.LetterStack.ReceiveLetter("DMS_BlastScanner_FoundLabel".Translate(),
                    "DMS_BlastScanner_FoundText".Translate(parent.LabelCap, list),
                    LetterDefOf.PositiveEvent, new LookTargets(firstLump, map));
            }
            else
            {
                Messages.Message("DMS_BlastScanner_NothingFound".Translate(parent.LabelCap),
                    parent, MessageTypeDefOf.NeutralEvent);
            }

            if (Rand.Chance(Props.infestationChance))
                TryWakeInsects(map);
        }

        private void PlayBlastEffects(Map map)
        {
            IntVec3 pos = parent.Position;
            (Props.detonateSound ?? DamageDefOf.Bomb.soundExplosion)?.PlayOneShot(new TargetInfo(pos, map));
            FleckMaker.Static(pos, map, FleckDefOf.ExplosionFlash, 6f);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(pos, 2.9f, true))
            {
                if (c.InBounds(map))
                    FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, Rand.Range(1.5f, 3f), new Color(0.6f, 0.55f, 0.5f));
            }
            if (map == Find.CurrentMap)
                Find.CameraDriver.shaker.DoShake(3f);
        }

        // 與原版 CompDeepScanner.DoFind 相同的散佈規則，但不發個別信件，由呼叫端彙整。
        // Same scatter rules as vanilla CompDeepScanner.DoFind, minus the per-lump letter.
        private static bool TryGenerateLump(Map map, out ThingDef lumpDef, out IntVec3 center)
        {
            lumpDef = null;
            if (!CellFinderLoose.TryFindRandomNotEdgeCellWith(10, c => CanScatterAt(c, map), map, out center))
                return false;

            lumpDef = DefDatabase<ThingDef>.AllDefs.Where(d => d.deepCommonality > 0f)
                .RandomElementByWeightWithFallback(d => d.deepCommonality);
            if (lumpDef == null) return false;

            int numCells = Mathf.CeilToInt(lumpDef.deepLumpSizeRange.RandomInRange);
            foreach (IntVec3 c in GridShapeMaker.IrregularLump(center, map, numCells))
            {
                if (CanScatterAt(c, map) && !c.InNoBuildEdgeArea(map))
                    map.deepResourceGrid.SetAt(c, lumpDef, lumpDef.deepCountPerCell);
            }
            return true;
        }

        private static bool CanScatterAt(IntVec3 c, Map map)
        {
            TerrainDef terrain = map.terrainGrid.BaseTerrainAt(c);
            if (terrain != null && terrain.IsWater && terrain.passability == Traversability.Impassable) return false;
            if (!c.GetAffordances(map).Contains(ThingDefOf.DeepDrill.terrainAffordanceNeeded)) return false;
            return !map.deepResourceGrid.GetCellBool(map.cellIndices.CellToIndex(c));
        }

        private void TryWakeInsects(Map map)
        {
            if (Faction.OfInsects == null) return;

            float points = StorytellerUtility.DefaultThreatPointsNow(map) * Props.infestationPointsFactor;
            int hiveCount = Mathf.Clamp(GenMath.RoundRandom(points / 220f), Props.hiveCountLimits.min, Props.hiveCountLimits.max);

            IntVec3 root = parent.Position;
            bool Valid(IntVec3 c)
            {
                float dist = c.DistanceTo(root);
                return dist >= Props.infestationDistance.min && dist <= Props.infestationDistance.max
                    && c.Standable(map) && !c.Fogged(map) && c.GetFirstBuilding(map) == null;
            }
            IntVec3? loc = null;
            if (CellFinder.TryFindRandomCellNear(root, map, Mathf.CeilToInt(Props.infestationDistance.max), Valid, out IntVec3 cell))
                loc = cell;

            // 找不到裝置旁的空地時交給原版挑地點，保證不會因為地形白白躲過。
            // No clear cell next to the scanner: let vanilla pick, so terrain can't dodge the swarm.
            Thing tunnel = InfestationUtility.SpawnTunnels(hiveCount, map, spawnAnywhereIfNoGoodCell: true,
                ignoreRoofedRequirement: true, overrideLoc: loc);
            if (tunnel == null) return;

            Find.LetterStack.ReceiveLetter("DMS_BlastScanner_InsectsLabel".Translate(),
                "DMS_BlastScanner_InsectsText".Translate(parent.LabelCap),
                LetterDefOf.ThreatBig, new LookTargets(tunnel));
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            parent.Map?.deepResourceGrid.MarkForDraw();
        }

        public override string CompInspectStringExtra()
        {
            if (FuseLit)
                return "DMS_BlastScanner_FuseBurning".Translate(fuseTicksLeft.ToStringTicksToPeriod());
            if (CooldownTicksLeft > 0)
                return "DMS_BlastScanner_Cooldown".Translate(CooldownTicksLeft.ToStringTicksToPeriod());
            return null;
        }
    }
}
