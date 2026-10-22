using RimWorld;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>一般索敵失敗後，讓 NPC 野禽主動破門。</summary>
    public class JobGiver_DroneAttackHostileDoor : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.Faction == null || pawn.Faction.IsPlayer || pawn.Map == null
                || CarrierAIUtility.FindNearbyHostile(pawn, 65f) != null)
            {
                return null;
            }
            Building_Door door = CarrierAIUtility.FindHostileDoor(pawn);
            if (door == null)
            {
                return null;
            }
            Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, door);
            job.expiryInterval = 120;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
