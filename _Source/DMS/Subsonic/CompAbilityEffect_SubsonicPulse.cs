using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 次聲波模組的技能：以施放者為中心放一次脈衝，只打敵對的血肉生物。
    /// 不要在 AbilityDef 上設 Ability_EffectRadius，否則原版會對範圍內每個目標各呼叫一次 Apply。
    /// Subsonic module ability: one pulse centred on the caster, hitting hostile flesh only.
    /// Don't set Ability_EffectRadius on the AbilityDef, or vanilla calls Apply once per target in range.
    /// </summary>
    public class CompProperties_AbilitySubsonicPulse : CompProperties_AbilityEffect
    {
        public SubsonicPulseProps pulse = new SubsonicPulseProps();

        /// <summary>AI 至少要有幾個敵人在範圍內才施放。Minimum hostiles in range before the AI casts.</summary>
        public int aiMinTargets = 2;

        public CompProperties_AbilitySubsonicPulse()
        {
            compClass = typeof(CompAbilityEffect_SubsonicPulse);
        }
    }

    public class CompAbilityEffect_SubsonicPulse : CompAbilityEffect
    {
        public new CompProperties_AbilitySubsonicPulse Props => (CompProperties_AbilitySubsonicPulse)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            if (caster?.Spawned != true) return;
            SubsonicUtility.DoPulse(caster.Map, caster.Position, Props.pulse, caster);
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            if (parent.pawn?.Spawned == true)
                GenDraw.DrawRadiusRing(parent.pawn.Position, Props.pulse.radius);
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            Pawn caster = parent.pawn;
            if (caster?.Spawned != true) return false;

            int count = 0;
            float radiusSq = Props.pulse.radius * Props.pulse.radius;
            var pawns = caster.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Dead || p.Downed || p.InMentalState || !p.RaceProps.IsFlesh) continue;
                if ((p.Position - caster.Position).LengthHorizontalSquared > radiusSq) continue;
                if (!p.HostileTo(caster)) continue;
                if (++count >= Props.aiMinTargets) return true;
            }
            return false;
        }
    }
}
