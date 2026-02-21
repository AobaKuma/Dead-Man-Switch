using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DMS
{
    // 研究文书的工作驱动
    public class JobDriver_ProcessQuestWorkable : JobDriver
    {
        private Thing TargetDoc => job.GetTarget(TargetIndex.A).Thing;
        private Thing TargetBench => job.GetTarget(TargetIndex.B).Thing;

        public CompQuestWorkable WorkableComp => TargetDoc?.TryGetComp<CompQuestWorkable>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 允许多人并发预约
            return pawn.Reserve(TargetDoc, job, 10, 1, null, errorOnFailed)
                && pawn.Reserve(TargetBench, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => WorkableComp == null || WorkableComp.IsCompleted);

            // 走向文书并拾取
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);

            // 前往研究台交互格
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B);

            // 在研究台处工作
            Toil workToil = ToilMaker.MakeToil("ProcessDocument");
            workToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(TargetBench);
                float statValue = pawn.GetStatValue(StatDefOf.ResearchSpeed, true);
                WorkableComp.DoWork(statValue, pawn);
                pawn.skills?.Learn(SkillDefOf.Intellectual, 0.1f);
                if (WorkableComp.IsCompleted)
                    ReadyForNextToil();
            };
            workToil.FailOnCannotTouch(TargetIndex.B, PathEndMode.InteractionCell);
            workToil.WithEffect(EffecterDefOf.Research, TargetIndex.B);
            // 头部显示进度条
            workToil.WithProgressBar(TargetIndex.A, () => WorkableComp?.ProgressPercent ?? 0f, false, -0.5f);
            workToil.defaultCompleteMode = ToilCompleteMode.Never;
            workToil.activeSkill = () => SkillDefOf.Intellectual;
            yield return workToil;
        }
    }


    // 分配工作
    public class WorkGiver_ProcessQuestWorkable : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways);
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            // 允许多个小人预约同一物理位置上的同叠物品
            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 10, 1, null, forced))
                return false;
            CompQuestWorkable comp = t.TryGetComp<CompQuestWorkable>();
            if (comp == null || comp.IsCompleted)
                return false;
            // 必须能找到研究台
            return FindBench(pawn) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Thing bench = FindBench(pawn);
            if (bench == null) return null;
            Job job = JobMaker.MakeJob(DMS_DefOf.DMS_ProcessQuestWorkable, t, bench);
            job.count = 1;
            return job;
        }

        // 查找地图上最近的可用研究台
        private Thing FindBench(Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
                PathEndMode.InteractionCell,
                TraverseParms.For(pawn),
                validator: t => t is Building_ResearchBench
                    && !t.IsForbidden(pawn)
                    && pawn.CanReserve(t));
        }
    }
}

