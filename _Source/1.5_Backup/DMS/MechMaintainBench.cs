using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace DMS
{
    public class JobDriver_GotoMaintenanceBench : JobDriver
    {
        Building Tar => TargetA.Thing as Building;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetA, job);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoCell(Tar.Position, PathEndMode.OnCell);
            Toil toil = Toils_General.WaitWith(TargetIndex.A, int.MaxValue);
            toil.WithEffect(EffecterDefOf.MechRepairing, TargetIndex.B);
            toil.PlaySustainerOrSound(SoundDefOf.RepairMech_Touch, 1f);
            toil.AddPreInitAction(delegate
            {
                ticksToNextRepair = 100;
            });
            toil.tickAction = delegate
            {
                if (--ticksToNextRepair <= 0)
                {
                    MechRepairUtility.RepairTick(pawn);
                    ticksToNextRepair = 100;
                }
            };
            toil.AddEndCondition(delegate
            {
                if (ticksToNextRepair == 90) {
                    if(!MechRepairUtility.CanRepair(pawn))
                        return JobCondition.Succeeded;
                    if(!Tar.TryGetComp<CompPowerTrader>().PowerOn)
                        return JobCondition.Incompletable;
                }
                return JobCondition.Ongoing;
            });
            yield return toil;
        }
        byte ticksToNextRepair;
    }
    public class ThinkNode_ConditionalNeedRepair : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            return MechRepairUtility.CanRepair(pawn);
        }
    }
    public class ThinkNode_GotoMaintenanceBay : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            foreach (var b in pawn.Map.listerThings.ThingsOfDef(MaintainDefOf.DMS_MechGestatorSmall))
            {
                if (b.TryGetComp<CompPowerTrader>().PowerOn && pawn.CanReserveAndReach(b,PathEndMode.OnCell,Danger.Deadly))
                {
                    return JobMaker.MakeJob(MaintainDefOf.DMS_GotoMaintenance, b,pawn);
                }
            }
            return null;
        }
    }
    [DefOf]
    static class MaintainDefOf
    {
        static MaintainDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MaintainDefOf));
        }
        public static JobDef DMS_GotoMaintenance;
        public static ThingDef DMS_MechGestatorSmall;
    }
    
}
