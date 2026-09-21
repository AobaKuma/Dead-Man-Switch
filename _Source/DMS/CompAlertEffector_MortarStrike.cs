using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using Verse;
using Verse.Sound;

namespace DMS
{
    /// <summary>
    /// 砲擊呼叫器的警報反應：收到警報後挑一名範圍內的敵對 pawn，把它的位置交給 FFF 的 AirSupport 系統
    /// （<see cref="AirSupportDef"/>）執行。砲彈種類、發數、散布、延遲、來向都在 AirSupportDef 裡設定。
    /// Alert response for the fire-support caller: on alarm, pick a hostile pawn in range and hand its
    /// position to FFF's AirSupport system (<see cref="AirSupportDef"/>). Projectile, count, spread,
    /// delay and direction of fire are all configured on the AirSupportDef.
    ///
    /// 砲彈是 flyOverhead 的投射物，落在厚屋頂（地下設施）下會被屋頂擋掉，所以只挑沒有厚屋頂的目標。
    /// The shells are flyOverhead projectiles and a thick roof (underground facilities) eats them, so
    /// only targets without a thick roof overhead are chosen.
    /// </summary>
    public class CompProperties_AlertEffector_MortarStrike : CompProperties_AlertEffector
    {
        public AirSupportDef airSupportDef;

        /// <summary>從呼叫器算起找目標的半徑。Radius around the caller to look for targets in.</summary>
        public float targetSearchRadius = 30f;

        /// <summary>觸發當下播的音效／特效（呼叫器本身）。Played at the caller when it fires the call.</summary>
        public SoundDef callSound;
        public EffecterDef callEffecter;

        public CompProperties_AlertEffector_MortarStrike()
        {
            compClass = typeof(CompAlertEffector_MortarStrike);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (airSupportDef == null) yield return $"{parentDef.defName}: CompProperties_AlertEffector_MortarStrike needs an airSupportDef";
        }
    }

    public class CompAlertEffector_MortarStrike : CompAlertEffector
    {
        public new CompProperties_AlertEffector_MortarStrike Props => (CompProperties_AlertEffector_MortarStrike)props;

        protected override void DoEffect()
        {
            if (!parent.Spawned || Props.airSupportDef == null) return;

            Map map = parent.Map;
            Faction ownFaction = AlertResponseUtility.ResolveFaction(this, null);

            // 範圍內的敵對 pawn，頭上還得沒有厚屋頂（砲彈穿不過）。
            // Hostile pawns in range without a thick roof overhead (the shells can't get through one).
            List<Pawn> targets = map.mapPawns.AllPawnsSpawned
                .Where(p => AlertResponseUtility.IsHostileTarget(p, ownFaction)
                            && p.Position.InHorDistOf(parent.Position, Props.targetSearchRadius)
                            && !(map.roofGrid.RoofAt(p.Position)?.isThickRoof ?? false))
                .ToList();
            if (targets.Count == 0) return;

            Pawn target = targets.RandomElement();
            Props.airSupportDef.Trigger(parent, map, target);

            (Props.callSound ?? SoundDefOf.FlickSwitch).PlayOneShot(new TargetInfo(parent.Position, map));
            Props.callEffecter?.Spawn(parent.Position, map).Cleanup();

            Messages.Message("DMS_MortarStrike_Triggered".Translate(parent.LabelCap),
                new LookTargets(parent), MessageTypeDefOf.ThreatBig);
        }
    }
}
