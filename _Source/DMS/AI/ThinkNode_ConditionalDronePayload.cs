using Fortified;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>
    /// 「還有無人機可打」的條件節點。
    ///
    /// 整套滯空戰術靠這個節點收尾：彈艙見底、空中也沒有自機無人機時條件不成立，
    /// 母艦就會退回一般機兵交戰行為直接壓上去。
    /// 沒有這個出口的話，一台放完貨的母艦會在射程邊緣無限風箏，仗永遠打不完。
    /// </summary>
    public class ThinkNode_ConditionalDronePayload : ThinkNode_Conditional
    {
        /// <summary>至少還能投放幾架才算「有貨」。</summary>
        public int minPayload = 1;

        /// <summary>彈艙空了，但空中還有自機無人機時是否仍算成立。</summary>
        public bool countLiveDrones = true;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            ThinkNode_ConditionalDronePayload obj = (ThinkNode_ConditionalDronePayload)base.DeepCopy(resolve);
            obj.minPayload = minPayload;
            obj.countLiveDrones = countLiveDrones;
            return obj;
        }

        protected override bool Satisfied(Pawn pawn)
        {
            CompMechPlatform platform = pawn.TryGetComp<CompMechPlatform>();
            if (platform == null)
            {
                return false;
            }

            int costPerPawn = platform.Props.costPerPawn;
            if (costPerPawn <= 0 || platform.IngredientCount >= costPerPawn * minPayload)
            {
                return true;
            }

            return countLiveDrones && platform.LiveSpawnedPawnCount > 0;
        }
    }
}
