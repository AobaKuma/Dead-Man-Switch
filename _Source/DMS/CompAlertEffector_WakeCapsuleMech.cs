using Fortified;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace DMS
{
    /// <summary>
    /// 封存艙被警報喚醒的機兵，改走設施增援同一套警報回應（<see cref="LordJob_AlarmResponse"/>）：
    /// 艙邊走不到警報位置時這次警報不喚醒（不消耗觸發機率與 oneShot）；醒來後前往警報位置，
    /// 之後每次警報重新判定，只打看得到的敵人。
    /// 釋放、陣營、音效與訊息都沿用 FFF 的 <see cref="CompAlertEffector_ReleaseCapsuleMech"/>，
    /// 這裡只把它掛上的 AssaultColony lord 換掉；醒來的陣營不敵對玩家時（FFF 給 DefendPoint）不動。
    ///
    /// Mechs a capsule releases on an alarm run the same alarm response as the facility reinforcements
    /// (<see cref="LordJob_AlarmResponse"/>). An alarm the capsule's surroundings can't reach doesn't wake it
    /// (neither the trigger roll nor oneShot is spent); once awake they head for the alarm, re-judge on every
    /// later alarm, and only fight what they can see. Release, faction,
    /// sound and message all stay with FFF's <see cref="CompAlertEffector_ReleaseCapsuleMech"/>; this only
    /// swaps out the AssaultColony lord it assigns. Wake-ups under a faction not hostile to the player (FFF
    /// gives those DefendPoint) are left alone.
    /// </summary>
    public class CompProperties_AlertEffector_WakeCapsuleMech : CompProperties_AlertEffector_ReleaseCapsuleMech
    {
        public CompProperties_AlertEffector_WakeCapsuleMech()
        {
            compClass = typeof(CompAlertEffector_WakeCapsuleMech);
        }
    }

    public class CompAlertEffector_WakeCapsuleMech : CompAlertEffector_ReleaseCapsuleMech
    {
        /// <summary>觸發這次 DoEffect 的警報位置，見 <see cref="CompAlertEffector_HoleEmerge"/>。The alarm behind the current DoEffect.</summary>
        private IntVec3 triggeringAlarmCell = IntVec3.Invalid;

        public override void Notify_SignalReceived(Signal signal)
        {
            triggeringAlarmCell = AlertResponseUtility.AlarmCellFor(this, signal);
            try
            {
                base.Notify_SignalReceived(signal);
            }
            finally
            {
                triggeringAlarmCell = IntVec3.Invalid;
            }
        }

        /// <summary>艙邊走不到警報位置就不喚醒，見 <see cref="CompAlertEffector_HoleEmerge"/>。A capsule whose surroundings can't reach the alarm stays asleep.</summary>
        protected override bool CanFire(Signal? signal)
        {
            return base.CanFire(signal) && AlertResponseUtility.CanReachAlarmFrom(this, signal,
                parent.OccupiedRect().ExpandedBy(1).Cells, WakeFaction());
        }

        /// <summary>醒來後的陣營，順序同 <see cref="Building_MechCapsule.ReleaseHostile"/>。The faction on wake-up, resolved as ReleaseHostile does.</summary>
        private Faction WakeFaction()
        {
            Faction faction = Props.spawnFactionDef != null ? Find.FactionManager.FirstFactionOfDef(Props.spawnFactionDef) : null;
            return faction ?? parent.Faction ?? (parent as Building_MechCapsule)?.Mech?.Faction ?? Faction.OfAncientsHostile;
        }

        protected override void DoEffect()
        {
            // 艙在 FFF 釋放後就銷毀了，先記下裡面的機兵。
            // FFF destroys the capsule on release, so note the mech first.
            Pawn mech = (parent as Building_MechCapsule)?.Mech;
            base.DoEffect();

            if (mech == null || !mech.Spawned || mech.Dead) return;
            if (!(mech.GetLord()?.LordJob is LordJob_AssaultColony)) return;

            AlertResponseUtility.JoinAlarmResponse(mech, triggeringAlarmCell, Props.listenSignal);
        }
    }
}
