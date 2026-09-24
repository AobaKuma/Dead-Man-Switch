using RimWorld;
using Verse;

namespace DMS
{
    public class CompProperties_HuntBreaker : CompProperties
    {
        /// <summary>首次暫停的年數（RimWorld 一年 = 60 天）。First suspension, in years (one RimWorld year = 60 days).</summary>
        public FloatRange suspendYears = new FloatRange(1f, 2f);

        /// <summary>
        /// 追殺已在暫停中時，這座節點被摧毀再疊加幾天；小於 0 = 再擲一次 suspendYears 疊加。
        /// Days stacked when the hunt is already suspended; below 0 = roll suspendYears again and stack that.
        /// </summary>
        public float stackDaysWhileSuspended = -1f;

        public CompProperties_HuntBreaker()
        {
            compClass = typeof(CompHuntBreaker);
        }

        /// <summary>本次摧毀要暫停／疊加的 ticks。Ticks this destruction suspends or stacks.</summary>
        public int RollTicks(bool alreadySuspended)
        {
            if (alreadySuspended && stackDaysWhileSuspended >= 0f)
            {
                return (int)(stackDaysWhileSuspended * GenDate.TicksPerDay);
            }
            return (int)(suspendYears.RandomInRange * GenDate.TicksPerYear);
        }
    }

    /// <summary>
    /// 殖民艦隊追緝網路上的節點（SAGE）。被打爛時暫停隱匿級的追殺；不解除永久敵對。
    /// A node in the colony fleet's tracking network (SAGE). Destroying it suspends the Occulted-tier hunt;
    /// it does not end the kill order.
    /// </summary>
    public class CompHuntBreaker : ThingComp
    {
        public CompProperties_HuntBreaker Props => (CompProperties_HuntBreaker)props;

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            // 建築不可拆除，只有打爛才算。Not deconstructible: only being destroyed counts.
            if (mode != DestroyMode.KillFinalize && mode != DestroyMode.KillFinalizeLeavingsOnly) return;
            OccultechSanctionUtility.TrySuspendHunt(Props, parent, previousMap);
        }

        public override string CompInspectStringExtra()
        {
            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            if (comp == null || !comp.PermanentHostile) return null;
            return comp.HuntSuspended
                ? "DMS_HuntBreaker_InspectSuspended".Translate(comp.HuntResumeTicksLeft.ToStringTicksToPeriod())
                : "DMS_HuntBreaker_InspectActive".Translate();
        }
    }
}
