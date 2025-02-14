using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Noise;
using Verse.Sound;
using CombatExtended;
using DMS;

namespace DMSCE
{
    public class CompAbilityEffect_ActiveProtectionSystem : CompAbilityEffect
    {
        private new CompProperties_ActiveProtectionSystem Props => (CompProperties_ActiveProtectionSystem)props;

        private Pawn Pawn => parent.pawn;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            tickRemain = Props.activeTicks;
            isActive = true;

        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            if (Pawn.Faction == Faction.OfPlayer)
            {
                return false;
            }
            if (parent.CanCast && !parent.Casting)
            {
                foreach (IntVec3 cell in GenAdj.OccupiedRect(Pawn).ExpandedBy(Props.Radius))
                {
                    List<Thing> list = Pawn.MapHeld.thingGrid.ThingsListAt(cell).Where((v) => v is ProjectileCE).ToList();
                    for (int i = 0; i < list.Count; i++)
                    {
                        Thing thing2 = list[i];
                        if (IsTargetProjectile(thing2))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        private bool isActive;
        protected int tickRemain;
        public override void CompTick()
        {
            if (!isActive) return;
            tickRemain--;
            if (tickRemain <= 0)
            {
                isActive = false;
            }
            if (!Pawn.Spawned) return;

            foreach (IntVec3 cell in GenAdj.OccupiedRect(Pawn).ExpandedBy(Props.Radius))
            {
                List<Thing> list = Pawn.Map.thingGrid.ThingsListAt(cell).Where((v) => v.GetType().IsSubclassOf(typeof(ProjectileCE)) && (v as ProjectileCE).launcher?.Faction != Pawn.Faction).ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    Thing thing2 = list[i];
                    if (IsTargetProjectile(thing2) && IsInBound(thing2 as ProjectileCE))
                    {
                        if (Rand.Range(0f, 1f) > Props.chanceToFail)
                        {
                            Vector3 pos = thing2.DrawPos;
                            FleckMaker.Static(Pawn.DrawPos + Rand.UnitVector3, Pawn.Map, FleckDefOf.ShotFlash, 3f);
                            for (int j = 0; j < 3; j++)
                            {
                                FleckCreationData dataStatic = FleckMaker.GetDataStatic(Pawn.DrawPos, Pawn.Map, Props.fleckDef);
                                dataStatic.spawnPosition = Vector3.Lerp(Pawn.DrawPos, pos, 0.4f);
                                dataStatic.scale = Rand.Range(0.5f, 1);
                                dataStatic.solidTimeOverride = 0f;
                                var noise = Rand.Range(-15, 15);
                                dataStatic.rotation = (dataStatic.spawnPosition - Pawn.DrawPos).AngleFlat() + noise;
                                dataStatic.velocityAngle = (dataStatic.spawnPosition - Pawn.DrawPos).AngleFlat() + noise;
                                dataStatic.velocitySpeed = 50f;
                                Pawn.Map.flecks.CreateFleck(dataStatic);
                            }
                            for (int k = 0; k < 3; k++)
                            {
                                float angle = (Vector3.Lerp(Pawn.DrawPos, pos, 0.2f) - Pawn.DrawPos).AngleFlat() + Rand.Range(-90f, 90f);

                                FleckCreationData dataStatic = FleckMaker.GetDataStatic(Pawn.DrawPos, Pawn.Map, FleckDefOf.AirPuff);
                                dataStatic.spawnPosition = Pawn.DrawPos + DMS.CircleConst.GetAngle(angle) * 2f;
                                dataStatic.scale = Rand.Range(1f, 4.9f);
                                dataStatic.rotationRate = Rand.Range(-30f, 30f) / dataStatic.scale;
                                dataStatic.velocityAngle = angle;
                                dataStatic.velocitySpeed = 5 - dataStatic.scale;
                                Pawn.Map.flecks.CreateFleck(dataStatic);
                            }
                            DoIntercept(thing2);
                            return;
                        }
                    }
                }
            }
        }
        public override string CompInspectStringExtra()
        {
            if (isActive)
                return "DMS_APS_TickRemain".Translate(tickRemain.TicksToSeconds());
            else
                return base.CompInspectStringExtra();
        }
        private void DoIntercept(Thing target)
        {
            if (target == null) return;
            var projectile = target as ProjectileCE;
            var velo = (projectile.Destination - projectile.origin).normalized.ToAngle() + 90;
            Log.Message(velo.ToString());

            if (Props.fleckDef != null)
            {
                if (Pawn.DrawPos.ShouldSpawnMotesAt(Pawn.Map, false))
                {
                    if (Props.spawnLeaving != null && Rand.Range(0f, 1f) > 0.8f)
                        GenSpawn.Spawn(Props.spawnLeaving, target.Position, target.Map);
                    var noise = Rand.Range(-5, 5);
                    for (int i = 0; i < 7; i++)
                    {
                        FleckCreationData dataStatic = FleckMaker.GetDataStatic(projectile.DrawPos, target.Map, Props.interceptedFleckDef);

                        dataStatic.targetSize = 0;
                        dataStatic.spawnPosition = projectile.DrawPos + CircleConst.GetAngle(velo + noise) * -2f;
                        dataStatic.rotation = velo + noise;
                        dataStatic.velocityAngle = velo + noise;
                        dataStatic.velocitySpeed = Rand.Range(target.def.projectile.speed / 2, target.def.projectile.speed);
                        dataStatic.scale = Rand.Range(2f, 5f) * (dataStatic.velocitySpeed / target.def.projectile.speed);
                        Pawn.Map.flecks.CreateFleck(dataStatic);
                    }
                }
            }
            projectile.ImpactSomething();
            Props.soundIntercepted.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map, false));
        }
        private bool IsInBound(Thing target)
        {
            var projectile = target as ProjectileCE;
            Vector2 destination = projectile.intendedTarget.CenterVector3;
            if (Vector2.Distance(destination, Pawn.DrawPos) < Props.Radius) return true;
            return false;
        }
        private bool IsTargetProjectile(Thing target)
        {
            if (target is null) return false;
            if (Props.ignoreThings.Contains(target.def.defName)) return false;
            if (!Props.interceptThings.Where((v => target.def.defName == v)).FirstOrDefault().NullOrEmpty()) return true;

            if (target is ProjectileCE_Explosive) return true;
            if (target.TryGetComp<CompExplosiveCE>() != null) return true;
            return false;
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isActive, "isActive", false);
            Scribe_Values.Look(ref tickRemain, "tickRemain", 100);
        }
    }
    public class CompProperties_ActiveProtectionSystem : CompProperties_AbilityEffect
    {
        public int Radius = 6;

        public FleckDef fleckDef;
        public ThingDef spawnLeaving;
        public FleckDef interceptedFleckDef;
        public SoundDef soundIntercepted;
        public float chanceToFail = 0.8f;
        public int activeTicks = 2400;
        public List<string> interceptThings = new List<string>();
        public List<string> ignoreThings = new List<string>();

        public CompProperties_ActiveProtectionSystem()
        {
            compClass = typeof(CompAbilityEffect_ActiveProtectionSystem);
        }
    }
}
