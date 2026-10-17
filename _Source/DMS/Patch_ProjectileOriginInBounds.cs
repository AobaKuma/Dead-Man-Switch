using HarmonyLib;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 1.7 飛行中的 pawn（例如遊隼）DrawPos 會往北偏移最多 0.6 格（Pawn_DrawTracker.FlyingOffset），
    /// 停在地圖最北一排開火時，子彈起點會落在地圖外。原版 Projectile 第一個 tick 就把 Position 設到界外，
    /// 噴出 register / de-register out of bounds 與 NRE，子彈也直接消失。這裡把發射起點夾回地圖內。
    /// In 1.7 a flying pawn's (e.g. Falcon) DrawPos is pushed up to 0.6 cells north (Pawn_DrawTracker.FlyingOffset),
    /// so firing from the map's top row launches the projectile from outside the map. Vanilla Projectile then sets its
    /// Position out of bounds on the first tick, logging register / de-register out-of-bounds errors plus an NRE, and
    /// the shot vanishes. Clamp the launch origin back inside the map.
    /// </summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch),
        new[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    public static class Patch_Projectile_LaunchOriginInBounds
    {
        private const float EdgeMargin = 0.01f;

        public static void Prefix(Projectile __instance, ref Vector3 origin)
        {
            Map map = __instance.Map;
            if (map == null || origin.InBounds(map))
            {
                return;
            }
            IntVec3 size = map.Size;
            origin.x = Mathf.Clamp(origin.x, 0f, size.x - EdgeMargin);
            origin.z = Mathf.Clamp(origin.z, 0f, size.z - EdgeMargin);
        }
    }
}
