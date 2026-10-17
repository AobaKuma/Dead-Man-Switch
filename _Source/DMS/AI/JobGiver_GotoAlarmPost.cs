using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace DMS
{
    /// <summary>
    /// 往勤務焦點（警報回應的崗位）移動，進到 <see cref="arriveRadius"/> 內就交給後面的徘徊節點。
    /// 移動工作會定期到期重想，讓前面的戰鬥節點在路上看到敵人時能插隊。
    ///
    /// Heads for the duty focus (the alarm-response post) and hands over to the wander node behind it
    /// once inside <see cref="arriveRadius"/>. The move job expires periodically so the fight node ahead
    /// of it can take over as soon as an enemy comes into view on the way.
    /// </summary>
    public class JobGiver_GotoAlarmPost : ThinkNode_JobGiver
    {
        /// <summary>離崗位這麼近就算到了。How close to the post counts as arrived.</summary>
        public float arriveRadius = 6f;

        public LocomotionUrgency locomotionUrgency = LocomotionUrgency.Jog;

        /// <summary>移動工作多久到期重想一次。How often the move job expires to re-think.</summary>
        public int expiryInterval = 120;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_GotoAlarmPost obj = (JobGiver_GotoAlarmPost)base.DeepCopy(resolve);
            obj.arriveRadius = arriveRadius;
            obj.locomotionUrgency = locomotionUrgency;
            obj.expiryInterval = expiryInterval;
            return obj;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            PawnDuty duty = pawn.mindState.duty;
            if (duty == null) return null;
            if (!duty.focus.IsValid)
            {
                // 剛落地、崗位還沒判定（PawnFlyer 落地就立刻想工作，比 LordToilTick 早）：現在判定，
                // 後面的徘徊節點也就拿得到有效的焦點。
                // Just landed with the post still pending (PawnFlyer thinks the instant it lands, ahead of
                // LordToilTick): judge it now, which also gives the wander node behind a valid focus.
                (pawn.GetLord()?.CurLordToil as LordToil_AlarmResponse)?.TryAssignPost(pawn);
                duty = pawn.mindState.duty;
                if (duty == null || !duty.focus.IsValid) return null;
            }

            IntVec3 post = duty.focus.Cell;
            if (pawn.Position.InHorDistOf(post, arriveRadius)) return null;
            // 路在判定後被切斷（門被鎖、牆被補上）就原地待命，交給徘徊節點。
            // If the way got cut after the post was assigned (door locked, wall rebuilt), hold and let the wander node take it.
            if (!pawn.CanReach(post, PathEndMode.Touch, Danger.Deadly)) return null;

            IntVec3 dest = RCellFinder.BestOrderedGotoDestNear(post, pawn);
            if (!dest.IsValid || dest == pawn.Position) return null;

            Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
            job.locomotionUrgency = locomotionUrgency;
            job.expiryInterval = expiryInterval;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
