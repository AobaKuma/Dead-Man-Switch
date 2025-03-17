using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Noise;
using Verse.Sound;

namespace DMS
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
                foreach (IntVec3 cell in GenAdj.OccupiedRect(Pawn).ExpandedBy(Props.Radius).ClipInsideMap(Pawn.Map))
                {
                    List<Thing> list = Pawn.MapHeld.thingGrid.ThingsListAt(cell).Where((v) => v is Projectile).ToList();
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
        protected int interceptCountMax = 2;
        protected int interceptCount;
        public override void CompTick()
        {
            if (!isActive) return;
            interceptCount = 0;
            foreach (IntVec3 cell in GenAdj.OccupiedRect(Pawn).ExpandedBy(Props.Radius).ClipInsideMap(Pawn.Map))
            {
                List<Thing> list = Pawn.MapHeld.thingGrid.ThingsListAt(cell).Where((v) => v is Projectile p && p.Launcher?.Faction != Pawn.Faction).ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    if (interceptCount >= interceptCountMax) return;
                    Thing thing2 = list[i];
                    if (IsTargetProjectile(thing2) && IsInBound(thing2 as Projectile))
                    {
                        interceptCount++;
                        if (Rand.Range(0f, 1f) > Props.chanceToFail)
                        {
                            if (Props.interceptedTargetEffecterDef != null)
                            {
                               Effecter effecter = Props.interceptedTargetEffecterDef.Spawn(targetA: Pawn.Position, targetB: thing2.Position, Pawn.Map);
                            }
                            else
                            {
                                Vector3 pos = thing2.DrawPos;
                                FleckMaker.Static(Pawn.DrawPos + Rand.UnitVector3, Pawn.Map, FleckDefOf.ShotFlash, 3f);
                                for (int j = 0; j < 3; j++)
                                {
                                    FleckCreationData dataStatic = FleckMaker.GetDataStatic(Pawn.DrawPos, Pawn.Map, Props.fleckDef);
                                    dataStatic.spawnPosition = Vector3.Lerp(Pawn.DrawPos, pos, 0.2f);
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
                                    dataStatic.spawnPosition = Pawn.DrawPos + CircleConst.GetAngle(angle) * 2f;
                                    dataStatic.scale = Rand.Range(1f, 4.9f);
                                    dataStatic.rotationRate = Rand.Range(-30f, 30f) / dataStatic.scale;
                                    dataStatic.velocityAngle = angle;
                                    dataStatic.velocitySpeed = 5 - dataStatic.scale;
                                    Pawn.Map.flecks.CreateFleck(dataStatic);
                                }
                            }
                            DoIntercept(thing2 as Projectile);
                        }
                    }
                }
            }
            tickRemain--;
            if (tickRemain <= 0)
            {
                isActive = false;
            }
        }
        public override string CompInspectStringExtra()
        {
            if (isActive)
                return "DMS_APS_TickRemain:{0}".Translate(tickRemain.TicksToSeconds());
            else
                return base.CompInspectStringExtra();
        }
        private void DoIntercept(Projectile target)
        {
            if (target == null) return;


            if (Props.fleckDef != null)
            {
                var projectile = target as Projectile;
                if (Pawn.DrawPos.ShouldSpawnMotesAt(Pawn.Map, false))
                {
                    if (Props.spawnLeaving != null)
                    {
                        if (Rand.Range(0f, 1f) > 0.8f)
                            GenSpawn.Spawn(Props.spawnLeaving, target.Position, target.Map);
                    }

                    FieldInfo origin = typeof(Projectile).GetField("origin", BindingFlags.NonPublic | BindingFlags.Instance);
                    var originVector = (Vector3)origin.GetValue(projectile);
                    FieldInfo destination = typeof(Projectile).GetField("destination", BindingFlags.NonPublic | BindingFlags.Instance);
                    var destinationVector = (Vector3)destination.GetValue(projectile);
                    var velo = (destinationVector - originVector).normalized;
                
                    if (target is Projectile_Explosive)
                    {
                        MethodInfo Explode = typeof(Projectile_Explosive).GetMethod("Explode", BindingFlags.NonPublic | BindingFlags.Instance);
                        Explode.Invoke(target, new object[] { });
                    }
                    for (int i = 0; i < 9; i++)
                    {
                        FleckCreationData dataStatic = FleckMaker.GetDataStatic(target.DrawPos, target.Map, Props.interceptedFleckDef);
                        dataStatic.scale = Rand.Range(2f, 5f);
                        var noise = Rand.Range(-15, 15); 
                        dataStatic.spawnPosition = target.DrawPos + CircleConst.GetAngle(velo.AngleFlat() + noise) * -2f;
                        dataStatic.rotation = velo.AngleFlat() + noise;
                        dataStatic.velocityAngle = velo.AngleFlat() + noise;
                        dataStatic.velocitySpeed = Rand.Range(target.def.projectile.speed / 3, target.def.projectile.speed / 2) / dataStatic.scale;
                        Pawn.Map.flecks.CreateFleck(dataStatic);
                    }
                }
            }
            Props.soundIntercepted.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map, false));
        }
        private bool IsInBound(Projectile target)
        {
            FieldInfo destinationInfo = typeof(Projectile).GetField("destination", BindingFlags.NonPublic | BindingFlags.Instance);
            Vector3 destination = (Vector3)destinationInfo.GetValue(target);
            if (Vector2.Distance(destination, Pawn.DrawPos) < Props.Radius) return true;
            return false;
        }
        private bool IsTargetProjectile(Thing target)
        {
            if (target is null) return false;
            if (target is Projectile_Explosive) return true;

            if (target.def.defName.Contains("rocket") || target.def.defName.Contains("missile") || target.def.defName.Contains("grenade"))
            {
                if (Props.ignoreThings.Contains(target.def.defName)) return false;
                return true;
            }
            if (!Props.interceptThings.Where((v => target.def.defName == v)).FirstOrDefault().NullOrEmpty()) return true;
            return false;
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isActive, "isActive", false);
            Scribe_Values.Look(ref tickRemain, "tickRemain", 100);
            Scribe_Values.Look(ref interceptCount, "interceptCount", 0);
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

        public EffecterDef interceptedTargetEffecterDef = null;
        public EffecterDef triggerEffecterDef = null;
        public EffecterDef launchMuzzleEffecterDef = null;

        public CompProperties_ActiveProtectionSystem()
        {
            compClass = typeof(CompAbilityEffect_ActiveProtectionSystem);
        }
    }
}
