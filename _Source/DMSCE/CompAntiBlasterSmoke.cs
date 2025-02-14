using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using UnityEngine;
using CombatExtended;
using System.Reflection;

namespace DMSCE
{
    public class CompAntiBlasterSmoke : ThingComp
    {
        [Unsaved(false)]
        private Effecter effecter;
        private CompProperties_AntiBlasterSmoke Props => (CompProperties_AntiBlasterSmoke)props;
        private bool isActive = true;
        private int tickRemain = 100;
        private Thing EffecterSourceThing
        {
            get
            {
                ThingWithComps pawn = parent;
                if (!parent.Spawned)
                {
                    IThingHolder parentHolder = parent.ParentHolder;
                    if (parentHolder != null)
                    {
                        if (parentHolder is Pawn_ApparelTracker pawn_ApparelTracker)
                        {
                            pawn = pawn_ApparelTracker.pawn;
                        }
                        else if (parentHolder is Pawn_CarryTracker pawn_CarryTracker)
                        {
                            pawn = pawn_CarryTracker.pawn;
                        }
                        else if (parentHolder is Pawn_EquipmentTracker pawn_EquipmentTracker)
                        {
                            pawn = pawn_EquipmentTracker.pawn;
                        }
                    }
                }

                return pawn;
            }
        }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            effecter?.Cleanup();
            effecter = null;
        }
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            tickRemain = Props.activeTicks;
        }
        public override void CompTick()
        {
            base.CompTick();
            if (!isActive) return;
            if (Props.effecterDef != null)
            {
                if (effecter == null)
                {
                    effecter = Props.effecterDef.Spawn();
                }

                effecter.EffectTick(EffecterSourceThing, TargetInfo.Invalid);
            }

            foreach (IntVec3 cell in GenAdj.OccupiedRect(parent).ExpandedBy(Props.Size))
            {
                List<Thing> list = parent.MapHeld.thingGrid.ThingsListAt(cell).Where((v) => v is ProjectileCE).ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    Thing thing2 = list[i];
                    if (IsTargetProjectile(thing2) && Vector3.Distance(thing2.DrawPos, parent.DrawPos) < Props.Size)
                    {
                        if (Rand.Range(0f, 1f) > Props.chanceToFail)
                            DoIntercept(thing2 as ProjectileCE);
                    }
                }
            }
            tickRemain--;
            if (tickRemain <= 0) isActive = false;
        }
        private void DoIntercept(ProjectileCE target)
        {
            if (target == null) return;
            if (Props.fleckDef != null)
            {
                if (parent.DrawPos.ShouldSpawnMotesAt(parent.Map, false))
                {
                    var projectile = target as ProjectileCE;
                    Vector3 velo = (projectile.Destination - projectile.origin).normalized;

                    FleckCreationData dataStatic = FleckMaker.GetDataStatic(target.DrawPos, parent.Map, Props.fleckDef, Rand.Range(0.5f, 1.5f));
                    dataStatic.rotation = target.Rotation.AsAngle;
                    dataStatic.targetSize = 0;
                    dataStatic.velocityAngle = velo.ToAngleFlat();
                    dataStatic.velocitySpeed = Rand.Range(target.def.projectile.speed / 2, target.def.projectile.speed);
                    dataStatic.scale = Rand.Range(1, 3);
                    parent.Map.flecks.CreateFleck(dataStatic);
                }
            }
            if (Props.spawnLeaving != null)
            {
                if (Rand.Range(0f, 1f) > 0.8f)
                    GenSpawn.Spawn(Props.spawnLeaving, target.Position, target.Map);
            }
            target.Destroy();
        }
        private bool IsTargetProjectile(Thing target)
        {
            if (target is null) return false;

            if (target is ProjectileCE)
            {
                if (target.def.defName.Contains("Charge") || target.def.defName.Contains("Blaster") || target.def.defName.Contains("Blaster"))
                {
                    if (Props.ignoreThings.Contains(target.def.defName)) return false;
                    return true;
                }
                if (!Props.interceptThings.Where((v => target.def.defName == v)).FirstOrDefault().NullOrEmpty()) return true;
            }
            return false;
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isActive, "isActive", true);
            Scribe_Values.Look(ref tickRemain, "tickRemain", 100);
        }
    }
    public class CompProperties_AntiBlasterSmoke : CompProperties
    {
        public int Size;
        public EffecterDef effecterDef;

        public FleckDef fleckDef;
        public ThingDef spawnLeaving;
        public float chanceToFail = 0.8f;
        public int activeTicks = 1500;
        public List<string> interceptThings;
        public List<string> ignoreThings;
        public CompProperties_AntiBlasterSmoke()
        {
            compClass = typeof(CompAntiBlasterSmoke);
        }
    }
}