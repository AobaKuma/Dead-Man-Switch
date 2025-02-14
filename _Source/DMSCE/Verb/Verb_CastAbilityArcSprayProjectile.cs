using RimWorld;
using RimWorld.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using CombatExtended;

namespace DMSCE
{
    public abstract class Verb_SprayCE : Verb_ShootCE
    {
        protected List<IntVec3> path = new List<IntVec3>();
        protected float targetShotAngle = 0;
        protected LocalTargetInfo originalTarget = LocalTargetInfo.Invalid;
        protected LocalTargetInfo originalTargetTemp = LocalTargetInfo.Invalid;

        public override float? AimAngleOverride
        {
            get
            {
                if (state == VerbState.Bursting && Available() && path.Any())
                {
                    return (path[Mathf.Min(path.Count, ShotsPerBurst - burstShotsLeft)].ToVector3Shifted() - caster.DrawPos).AngleFlat();
                }
                return null;
            }
        }
        public override bool TryCastShot()
        {
            currentTarget = new LocalTargetInfo(path[ShotsPerBurst - burstShotsLeft]);
            ShiftTarget(ShiftVecReportFor(currentTarget), false, false, false);
            shotAngle = targetShotAngle;
            originalTargetTemp = originalTarget;

            var b = base.TryCastShot();

            originalTargetTemp = LocalTargetInfo.Invalid;

            return b;
        }

        public override bool TryFindCEShootLineFromTo(IntVec3 root, LocalTargetInfo targ, out ShootLine resultingLine)
        {
            return base.TryFindCEShootLineFromTo(root, originalTargetTemp.IsValid ? originalTarget : targ, out resultingLine);
        }

        public override void WarmupComplete()
        {
            originalTarget = currentTarget;
            burstShotsLeft = ShotsPerBurst;
            PreparePath();

            base.WarmupComplete();

            ShiftTarget(ShiftVecReportFor(currentTarget), false, false, false);
            targetShotAngle = shotAngle;
            state = VerbState.Bursting;
            TryCastNextBurstShot();
        }

        protected abstract void PreparePath();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref path, "path", LookMode.Value);
            Scribe_Values.Look(ref targetShotAngle, "targetShotAngle");
            Scribe_TargetInfo.Look(ref originalTarget, "originalTarget");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && path == null)
            {
                path = new List<IntVec3>();
            }
        }
    }

    public class Verb_ArcSprayCE : Verb_SprayCE
    {
        protected override void PreparePath()
        {
            path.Clear();
            Vector3 normalized = (currentTarget.CenterVector3 - caster.Position.ToVector3Shifted()).Yto0().normalized;
            Vector3 tan = normalized.RotatedBy(90f);
            for (int i = 0; i < burstShotsLeft; i++)
            {
                for (int j = 0; j < 15; j++)
                {
                    float rand = Rand.Value - 0.5f;
                    float width = Rand.Value * verbProps.sprayWidth * 2f - verbProps.sprayWidth;
                    float num3 = rand * verbProps.sprayThicknessCells + rand * 2f * verbProps.sprayArching;
                    IntVec3 item = (currentTarget.CenterVector3 + width * tan - num3 * normalized).ToIntVec3();
                    if (!path.Contains(item) || Rand.Value < 0.25f)
                    {
                        caster.Map.debugDrawer.FlashCell(item);
                        path.Add(item);
                        break;
                    }
                }
            }
            path.Add(currentTarget.Cell);
            path.SortBy((IntVec3 c) => (c.ToVector3Shifted() - caster.DrawPos).Yto0().normalized.AngleToFlat(tan));
        }
    }
    public class Verb_CastAbilityArcSprayProjectile : Verb_ArcSprayCE, IAbilityVerb
    {
        private Ability ability;

        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if(base.ValidateTarget(target, showMessages)) return true;
            return false;
        }
        public override bool TryCastShot()
        {
            return base.TryCastShot();
        }
        protected override bool OnCastSuccessful()
        {
            Ability.StartCooldown(ability.def.cooldownTicksRange.RandomInRange);
            return base.OnCastSuccessful();
        }
        public override bool Available()
        {
            return base.Available() && (Bursting||Ability.CanCast);
        }
        public override int ShotsPerBurst => verbProps.burstShotCount;
        public override ThingDef Projectile => verbProps.defaultProjectile;
        public Ability Ability
        {
            get
            {
                return ability;
            }
            set
            {
                ability = value;
            }
        }
        public override void WarmupComplete()
        {
            base.WarmupComplete();
        }
        public override void ExposeData()
        {
            Scribe_References.Look(ref ability, "ability");
            base.ExposeData();
        }
    }
}