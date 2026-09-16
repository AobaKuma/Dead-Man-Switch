using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>
    /// 母艦滯空交戰。
    ///
    /// 集群織鳥沒有裝甲，它的火力來自兩具會自己找目標的副砲塔與整艙自殺無人機，
    /// 所以它不該像一般機兵那樣壓到臉上互射。這個節點讓它把自己維持在
    /// 「自身射程邊緣、對方槍夠不到」的高度帶上，只有距離跑掉時才移動。
    ///
    /// 所有距離都由 <see cref="CarrierAIUtility.EffectiveReach"/> 換算，
    /// 原版與 CE 不必各寫一套數值。
    /// </summary>
    public class JobGiver_AICarrierStandoff : JobGiver_AIFightEnemy
    {
        /// <summary>期望滯空距離＝有效射程 × 此係數。</summary>
        public float standoffFactor = 0.85f;

        /// <summary>滯空距離上限＝有效射程 × 此係數，避免退到自己也打不到。</summary>
        public float maxReachFactor = 0.98f;

        /// <summary>射程極短時的距離下限，免得整個帶塌到貼臉。</summary>
        public float minStandoff = 12f;

        /// <summary>遲滯區間半寬：距離還在這個帶內就不重新找位置。</summary>
        public float bandWidth = 6f;

        /// <summary>是否參考附近敵人的射程，盡量待在對方打不到的地方。</summary>
        public bool respectHostileRange = true;

        /// <summary>退到敵人射程外時額外多留的餘裕。</summary>
        public float hostileRangeMargin = 4f;

        /// <summary>血量低於此比例時把期望距離往上拉；設 0 為停用。</summary>
        public float damagedHealthThreshold = 0f;

        /// <summary>受損後額外拉開的距離。</summary>
        public float damagedStandoffBonus = 8f;

        /// <summary>單次重新定位的搜尋半徑（也等於它一次願意移動多遠）。</summary>
        public float repositionSearchRadius = 26f;

        /// <summary>一把遠程武裝都找不到時採用的射程。</summary>
        public float fallbackReach = 25f;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_AICarrierStandoff obj = (JobGiver_AICarrierStandoff)base.DeepCopy(resolve);
            obj.standoffFactor = standoffFactor;
            obj.maxReachFactor = maxReachFactor;
            obj.minStandoff = minStandoff;
            obj.bandWidth = bandWidth;
            obj.respectHostileRange = respectHostileRange;
            obj.hostileRangeMargin = hostileRangeMargin;
            obj.damagedHealthThreshold = damagedHealthThreshold;
            obj.damagedStandoffBonus = damagedStandoffBonus;
            obj.repositionSearchRadius = repositionSearchRadius;
            obj.fallbackReach = fallbackReach;
            return obj;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // 正在開艙投放時不要插手，否則放到一半的動作會被打斷。
            if (pawn.CurJobDef == DMS_DefOf.DMS_DeployDroneSwarm)
            {
                return null;
            }

            UpdateEnemyTarget(pawn);
            Thing target = pawn.mindState.enemyTarget;
            if (target == null || !target.Spawned)
            {
                return null;
            }
            if (target is Pawn targetPawn && targetPawn.IsPsychologicallyInvisible())
            {
                return null;
            }

            float reach = CarrierAIUtility.EffectiveReach(pawn, fallbackReach);
            float desired = DesiredDistance(pawn, reach);
            float holdMax = Mathf.Min(desired + bandWidth, reach * maxReachFactor);
            float holdMin = Mathf.Max(1f, desired - bandWidth);
            if (holdMax < holdMin)
            {
                holdMax = holdMin;
            }

            float dist = pawn.Position.DistanceTo(target.Position);
            Verb verb = pawn.TryGetAttackVerb(target, allowManualCastWeapons: true, allowTurrets);
            bool hasRangedVerb = verb != null && !verb.verbProps.IsMeleeAttack;
            bool inBand = dist >= holdMin && dist <= holdMax;

            // 已經在帶內而且打得到（或根本沒有主武器、火力全交給副砲塔）就原地待命。
            // Wait_Combat 會自動朝視野內的目標開火。
            if (inBand && (!hasRangedVerb || verb.CanHitTarget(target)))
            {
                return HoldPositionJob(pawn);
            }

            // 帶內但沒有射界，或距離跑掉了：找新的滯空點。
            if (CarrierAIUtility.TryFindStandoffCell(pawn, target, holdMin, holdMax, desired,
                repositionSearchRadius, out IntVec3 dest))
            {
                if (dest == pawn.Position)
                {
                    return HoldPositionJob(pawn);
                }

                // 已經在往同一個還算數的位置移動，就不要每 30 tick 重下一次指令。
                Job curJob = pawn.CurJob;
                if (curJob != null && curJob.def == JobDefOf.Goto && curJob.jobGiver == this
                    && DestinationStillValid(curJob.targetA.Cell, target.Position, holdMin, holdMax))
                {
                    return curJob;
                }

                Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
                job.locomotionUrgency = LocomotionUrgency.Jog;
                job.expiryInterval = ExpiryInterval_ShooterSucceeded.RandomInRange;
                job.checkOverrideOnExpire = true;
                return job;
            }

            // 帶內卻找不到更好的位置：至少別亂跑。
            if (inBand)
            {
                return HoldPositionJob(pawn);
            }

            // 距離太遠、或被逼到角落退無可退：交還給下面的節點（陣營勤務／一般機兵交戰）處理，
            // 由它負責把母艦帶到有效距離，或在無路可退時正面應戰。
            return null;
        }

        /// <summary>
        /// 原地待命開火。常駐思考樹每 30 tick 就會重跑一次，若每次都發新的 Wait_Combat，
        /// 工作會被不斷重啟、武器的預熱也跟著歸零，母艦等於永遠打不出第一發。
        /// 因此已經在待命中時直接把「同一個」工作交回去，讓工作追蹤器判定不需要切換。
        /// </summary>
        private Job HoldPositionJob(Pawn pawn)
        {
            Job curJob = pawn.CurJob;
            if (curJob != null && curJob.def == JobDefOf.Wait_Combat && curJob.jobGiver == this)
            {
                return curJob;
            }
            pawn.pather?.StopDead();
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, ExpiryInterval_ShooterSucceeded.RandomInRange,
                checkOverrideOnExpiry: true);
        }

        private static bool DestinationStillValid(IntVec3 cell, IntVec3 targetCell, float holdMin, float holdMax)
        {
            if (!cell.IsValid)
            {
                return false;
            }
            float d = (cell - targetCell).LengthHorizontal;
            return d >= holdMin && d <= holdMax;
        }

        private float DesiredDistance(Pawn pawn, float reach)
        {
            float desired = Mathf.Max(minStandoff, reach * standoffFactor);

            if (respectHostileRange)
            {
                float hostileRange = CarrierAIUtility.HostileMaxRange(pawn, reach + 20f);
                if (hostileRange > 0f)
                {
                    desired = Mathf.Max(desired, hostileRange + hostileRangeMargin);
                }
            }

            if (damagedHealthThreshold > 0f
                && pawn.health?.summaryHealth != null
                && pawn.health.summaryHealth.SummaryHealthPercent < damagedHealthThreshold)
            {
                desired += damagedStandoffBonus;
            }

            // 再怎麼想拉開，也不能退到自己的武器構不著。
            return Mathf.Clamp(desired, 2f, Mathf.Max(2f, reach * maxReachFactor));
        }

        /// <summary>
        /// 基底類別要求的實作。這裡沿用同一套滯空點搜尋，
        /// 讓任何走 base.TryGiveJob 的路徑也不會把母艦帶進近距離。
        /// </summary>
        protected override bool TryFindShootingPosition(Pawn pawn, out IntVec3 dest, Verb verbToUse = null)
        {
            dest = IntVec3.Invalid;
            Thing target = pawn.mindState.enemyTarget;
            if (target == null || !target.Spawned)
            {
                return false;
            }
            float reach = CarrierAIUtility.EffectiveReach(pawn, fallbackReach);
            float desired = DesiredDistance(pawn, reach);
            float holdMax = Mathf.Min(desired + bandWidth, reach * maxReachFactor);
            float holdMin = Mathf.Max(1f, desired - bandWidth);
            if (holdMax < holdMin)
            {
                holdMax = holdMin;
            }
            return CarrierAIUtility.TryFindStandoffCell(pawn, target, holdMin, holdMax, desired,
                repositionSearchRadius, out dest);
        }
    }
}
