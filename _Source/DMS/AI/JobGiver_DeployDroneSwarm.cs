using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>
    /// 接敵時才放無人機。
    ///
    /// 框架原本給 NPC 平台的是「上場就按碼表一直放」，母艦還在半張地圖外
    /// 就把彈艙倒光，等真的打起來反而沒東西可投。這個節點把投放綁在
    /// 「已經有敵人進入投放半徑、而且空中的機數還不夠」兩個條件上。
    ///
    /// 搭配 CompProperties_MechPlatform 的 npcAutoDeploy=false 使用。
    /// </summary>
    public class JobGiver_DeployDroneSwarm : ThinkNode_JobGiver
    {
        /// <summary>投放半徑＝有效射程 × 此係數。</summary>
        public float triggerRadiusFactor = 1.4f;

        /// <summary>投放半徑下限。</summary>
        public float minTriggerRadius = 20f;

        /// <summary>空中同時存在的自機無人機上限，到頂就先不補。</summary>
        public int maxLiveDrones = 8;

        /// <summary>一把遠程武裝都找不到時採用的射程。</summary>
        public float fallbackReach = 25f;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_DeployDroneSwarm obj = (JobGiver_DeployDroneSwarm)base.DeepCopy(resolve);
            obj.triggerRadiusFactor = triggerRadiusFactor;
            obj.minTriggerRadius = minTriggerRadius;
            obj.maxLiveDrones = maxLiveDrones;
            obj.fallbackReach = fallbackReach;
            return obj;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // 已經在投放了就把同一個工作交回去。常駐思考樹每 30 tick 重跑一次，
            // 每次都發新工作會讓開艙動作永遠重來、一架也放不出去。
            if (pawn.CurJobDef == DMS_DefOf.DMS_DeployDroneSwarm)
            {
                return pawn.CurJob;
            }

            CompMechPlatform platform = pawn.TryGetComp<CompMechPlatform>();
            if (platform == null || !platform.CanSpawn.Accepted)
            {
                return null;
            }
            if (platform.LiveSpawnedPawnCount >= maxLiveDrones)
            {
                return null;
            }

            float reach = CarrierAIUtility.EffectiveReach(pawn, fallbackReach);
            float radius = Mathf.Max(minTriggerRadius, reach * triggerRadiusFactor);

            Thing target = pawn.mindState.enemyTarget;
            if (target == null || !target.Spawned || target.Map != pawn.Map
                || !pawn.Position.InHorDistOf(target.Position, radius))
            {
                target = CarrierAIUtility.FindNearbyHostile(pawn, radius);
            }
            if (target == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(DMS_DefOf.DMS_DeployDroneSwarm, target);
            job.locomotionUrgency = LocomotionUrgency.None;
            return job;
        }
    }
}
