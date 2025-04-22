using System;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DMS
{
    public class AirSupportData_LaunchProjectile : AirSupportData
    {
        public ThingDef projectileDef;
        public Vector3 origin;
        public LocalTargetInfo usedTarget = LocalTargetInfo.Invalid;
        public SoundDef soundDef;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref projectileDef, "projectileDef");
            Scribe_Values.Look(ref origin, "origin");
            Scribe_Defs.Look(ref soundDef, "soundDef");
            Scribe_TargetInfo.Look(ref usedTarget, "usedTarget");
        }

        public override void Trigger()
        {
            Projectile projectile = (Projectile)GenSpawn.Spawn(projectileDef, origin.ToIntVec3(), map);
            projectile.Launch(triggerer, origin, usedTarget.IsValid ? usedTarget : target, target, ProjectileHitFlags.IntendedTarget);
            soundDef?.PlayOneShot(SoundInfo.InMap(new TargetInfo(origin.ToIntVec3(), map)));
        }
    }
    public class AirSupportData_LaunchProjectileOnEdge : AirSupportData_LaunchProjectile
    {
        public override void Trigger()
        {
            var xEdge = origin.x > target.Cell.x ? map.Size.x - 0.01f : 0.01f;
            var zEdge = origin.z > target.Cell.z ? map.Size.z - 0.01f : 0.01f;

            var xDifference = Math.Abs((xEdge - target.Cell.x) / (origin.x - target.Cell.x));
            var zDifference = Math.Abs((-target.Cell.z) / (origin.z - target.Cell.z));

            string str = $"prev origin {origin}, target {target.Cell}, difference {xDifference},{zDifference}";

            var deltaZ = origin - target.Cell.ToVector3Shifted();

            str += $", delta {deltaZ}";

            var deltaX = deltaZ * xDifference + target.Cell.ToVector3Shifted();
            deltaX.x = xEdge;

            deltaZ *= zDifference;
            deltaZ += target.Cell.ToVector3Shifted();
            deltaZ.z = zEdge;


            if (deltaX.InBounds(map))
            {
                origin = deltaX;
            }
            else
            {
                origin = deltaZ;
            }
            str += $",x {deltaX.ToIntVec3()}, z {deltaZ.ToIntVec3()}, cur origin {origin}";
            try
            {
                base.Trigger();
            }
            catch
            {
                Log.Error(str);
            }
        }
    }

    public class AirSupportData_LaunchProjectileFromPlane : AirSupportData_LaunchProjectile, IAttachedToFlyBy
    {
        public FlyByThing plane;

        public Vector3 offset;

        FlyByThing IAttachedToFlyBy.plane
        {
            set => plane = value;
        }

        public override void Trigger()
        {
            if (plane != null && plane.Spawned) origin = plane.DrawPos + offset.RotatedBy(plane.angle);
            origin.x = Mathf.Clamp(origin.x, 0, map.Size.x - 1.01f);
            origin.z = Mathf.Clamp(origin.z, 0, map.Size.z - 1.01f);
            base.Trigger();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref plane, "plane");
            Scribe_Values.Look(ref offset, "offset");
        }
    }
}
