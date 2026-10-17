using Verse;

namespace DMS
{
    /// <summary>
    /// 次聲波手雷的脈衝參數，掛在投射物 ThingDef 上。
    /// Pulse parameters for the subsonic grenade, placed on the projectile ThingDef.
    /// </summary>
    public class ModExtension_SubsonicPulse : DefModExtension
    {
        public SubsonicPulseProps pulse = new SubsonicPulseProps();
    }

    /// <summary>
    /// 次聲波手雷：落地延遲後不做一般爆炸，而是放一次不看視線、不分敵我的脈衝。
    /// 爆炸傷害與炸牆都沒有，所以不走 GenExplosion。
    /// Subsonic grenade: after the fuse it skips the regular explosion and fires one pulse that
    /// ignores walls and friend-or-foe. No blast damage, no wall breaching, so no GenExplosion.
    /// </summary>
    public class Projectile_SubsonicGrenade : Projectile_Explosive
    {
        protected override void Explode()
        {
            Map map = Map;
            IntVec3 center = Position;
            SubsonicPulseProps pulse = def.GetModExtension<ModExtension_SubsonicPulse>()?.pulse;
            Thing instigator = launcher;
            Destroy();

            if (pulse == null)
            {
                Log.ErrorOnce($"[DMS] {def.defName} has no ModExtension_SubsonicPulse.", def.shortHash ^ 0x5B5B);
                return;
            }
            SubsonicUtility.DoPulse(map, center, pulse, instigator);
        }
    }
}
