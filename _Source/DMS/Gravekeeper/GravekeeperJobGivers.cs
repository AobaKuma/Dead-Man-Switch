using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace DMS
{
    public static class GravekeeperAIUtility
    {
        public static LordJob_GravekeeperPatrol PatrolOf(Pawn pawn)
        {
            return pawn?.GetLord()?.LordJob as LordJob_GravekeeperPatrol;
        }
    }

    /// <summary>
    /// 不知怎麼進了電梯廳（被擊退、從洞口落地……）就先走回最近的路線點。守墓人不待在出口。
    /// Somehow inside the lift lobby (knocked back, landed there, …): walk back to the nearest route point first.
    /// Gravekeepers don't stand at the exit.
    /// </summary>
    public class JobGiver_GravekeeperLeaveLobby : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            LordJob_GravekeeperPatrol patrol = GravekeeperAIUtility.PatrolOf(pawn);
            if (patrol == null || !patrol.InLobby(pawn.Position)) return null;

            int index = patrol.NearestRouteIndex(pawn.Position);
            if (index < 0) return null;
            IntVec3 dest = patrol.route[index];
            if (!pawn.CanReach(dest, PathEndMode.OnCell, Danger.Deadly)) return null;

            Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            job.expiryInterval = 120;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    /// <summary>
    /// 往勤務焦點移動。焦點是格子就走到 <see cref="arriveRadius"/> 內；焦點是 pawn（領隊）就跟到勤務半徑內，
    /// 落後太多時小跑追上。目的地永遠不在電梯廳裡。
    /// Moves towards the duty focus. A cell focus is approached to within <see cref="arriveRadius"/>; a pawn focus
    /// (the leader) is followed to within the duty radius, jogging to catch up when far behind. The destination is
    /// never inside the lift lobby.
    /// </summary>
    public class JobGiver_GravekeeperMove : ThinkNode_JobGiver
    {
        public LocomotionUrgency locomotionUrgency = LocomotionUrgency.Walk;

        public float arriveRadius = 2f;

        /// <summary>移動工作多久到期重想，讓前面的節點能插隊。How often the move job expires so the nodes ahead can step in.</summary>
        public int expiryInterval = 150;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_GravekeeperMove obj = (JobGiver_GravekeeperMove)base.DeepCopy(resolve);
            obj.locomotionUrgency = locomotionUrgency;
            obj.arriveRadius = arriveRadius;
            obj.expiryInterval = expiryInterval;
            return obj;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            PawnDuty duty = pawn.mindState.duty;
            if (duty == null || !duty.focus.IsValid) return null;

            LordJob_GravekeeperPatrol patrol = GravekeeperAIUtility.PatrolOf(pawn);
            Pawn followee = duty.focus.Thing as Pawn;
            IntVec3 target;
            float radius;
            if (followee != null)
            {
                if (followee == pawn || !followee.Spawned) return null;
                target = followee.Position;
                radius = duty.radius > 0f ? duty.radius : arriveRadius;
            }
            else
            {
                target = duty.focus.Cell;
                radius = arriveRadius;
            }

            if (pawn.Position.InHorDistOf(target, radius)) return null;
            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly)) return null;

            IntVec3 dest = RCellFinder.BestOrderedGotoDestNear(target, pawn, c => patrol == null || !patrol.InLobby(c));
            if (!dest.IsValid || dest == pawn.Position) return null;

            Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
            job.locomotionUrgency = followee != null && !pawn.Position.InHorDistOf(target, radius * 2f)
                ? LocomotionUrgency.Jog
                : locomotionUrgency;
            job.expiryInterval = expiryInterval;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    /// <summary>
    /// 原地站崗並面向勤務焦點（盤查時就是入侵者）。用自己的 <see cref="JobDriver_GravekeeperHold"/>，
    /// 因為原版的 Wait 工作會自動近戰相鄰的敵人，會打破「不開第一槍」。
    /// Holds position facing the duty focus (the intruder while challenging). It uses its own
    /// <see cref="JobDriver_GravekeeperHold"/> because vanilla Wait jobs melee any adjacent enemy on their own,
    /// which would break the hold-fire rule.
    /// </summary>
    public class JobGiver_GravekeeperHold : ThinkNode_JobGiver
    {
        public int expiryInterval = 90;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_GravekeeperHold obj = (JobGiver_GravekeeperHold)base.DeepCopy(resolve);
            obj.expiryInterval = expiryInterval;
            return obj;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            LocalTargetInfo focus = pawn.mindState.duty?.focus ?? LocalTargetInfo.Invalid;
            Job job = JobMaker.MakeJob(DMS_DefOf.DMS_GravekeeperHold, focus);
            job.expiryInterval = expiryInterval;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    /// <summary>
    /// 交戰：原版的戰鬥節點，再加上「不出設施」的限制。看得到的目標照打（包括電梯廳裡的），
    /// 但不會為了打人走進電梯廳：移動、近戰與跳躍落點落在電梯廳的工作一律放棄。
    /// Engagement: the vanilla fight node plus the stay-inside rule. Anything in sight is a fair target (the lift
    /// lobby included), but nothing walks into the lobby to get at it: moves, melee and jump landings that end up in
    /// the lobby are dropped.
    /// </summary>
    public class JobGiver_GravekeeperFight : JobGiver_AIFightEnemies
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            LordJob_GravekeeperPatrol patrol = GravekeeperAIUtility.PatrolOf(pawn);
            if (patrol != null && patrol.InLobby(pawn.Position)) return null;

            Job job = base.TryGiveJob(pawn);
            if (job == null || patrol == null) return job;

            if (job.def == JobDefOf.Goto && patrol.InLobby(job.targetA.Cell)) return null;
            if (job.def == JobDefOf.AttackMelee && job.targetA.IsValid && patrol.InLobby(job.targetA.Cell)) return null;
            if (job.ability?.verb is Verb_CastAbilityJump && job.targetA.IsValid && patrol.InLobby(job.targetA.Cell)) return null;
            return job;
        }
    }

    /// <summary>
    /// 站崗：停在原地、面向焦點，什麼都不打。結束靠工作到期（由 <see cref="JobGiver_GravekeeperHold"/> 設定）。
    /// Standing watch: stays put, faces the focus and attacks nothing. It ends when the job expires
    /// (set by <see cref="JobGiver_GravekeeperHold"/>).
    /// </summary>
    public class JobDriver_GravekeeperHold : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, pawn.Position);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil hold = ToilMaker.MakeToil("GravekeeperHold");
            hold.initAction = () => pawn.pather?.StopDead();
            hold.tickAction = () =>
            {
                LocalTargetInfo focus = job.targetA;
                if (!focus.IsValid || (focus.HasThing && !focus.Thing.Spawned)) return;
                if (focus.Cell != pawn.Position) pawn.rotationTracker.FaceTarget(focus);
            };
            hold.handlingFacing = true;
            hold.defaultCompleteMode = ToilCompleteMode.Never;
            yield return hold;
        }
    }
}
