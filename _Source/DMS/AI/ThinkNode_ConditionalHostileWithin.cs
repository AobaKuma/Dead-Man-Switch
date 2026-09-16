using UnityEngine;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>
    /// 「附近有敵人」的條件節點，半徑可以直接跟著單位自己的射程走。
    /// 用來當整段母艦戰術的總開關：地圖上沒有威脅時完全不介入，
    /// 行軍、集結、撤離都還是交給原本的陣營勤務。
    /// </summary>
    public class ThinkNode_ConditionalHostileWithin : ThinkNode_Conditional
    {
        /// <summary>固定半徑；設 0 表示只用 reachFactor 換算。</summary>
        public float radius = 0f;

        /// <summary>半徑＝有效射程 × 此係數，與 radius 取大者。</summary>
        public float reachFactor = 1.6f;

        /// <summary>一把遠程武裝都找不到時採用的射程。</summary>
        public float fallbackReach = 25f;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            ThinkNode_ConditionalHostileWithin obj = (ThinkNode_ConditionalHostileWithin)base.DeepCopy(resolve);
            obj.radius = radius;
            obj.reachFactor = reachFactor;
            obj.fallbackReach = fallbackReach;
            return obj;
        }

        protected override bool Satisfied(Pawn pawn)
        {
            if (pawn.Map == null)
            {
                return false;
            }
            float reach = CarrierAIUtility.EffectiveReach(pawn, fallbackReach);
            float r = Mathf.Max(radius, reach * reachFactor);
            return CarrierAIUtility.FindNearbyHostile(pawn, r) != null;
        }
    }
}
