using System.Collections.Generic;
using System.Linq;
using Fortified;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 標記一個追殺依賴殖民艦隊的 SAGE 追緝網路。摧毀 SAGE 節點的真空期會一併暫停所有帶此標記的追殺。
    /// Marks a huntdown as relying on the colonial fleet's SAGE tracking network; a SAGE blackout suspends all of them.
    /// </summary>
    public class ModExtension_FleetTracking : DefModExtension
    {
    }

    /// <summary>
    /// 艦隊追緝網路的真空期。所有帶 <see cref="ModExtension_FleetTracking"/> 的追殺共用同一個真空期：
    /// 同時暫停、同時恢復；真空期中才啟動的追殺立即承接剩餘時間。
    /// The fleet tracking network's blackout. Every huntdown carrying <see cref="ModExtension_FleetTracking"/> shares it:
    /// they suspend and resume together, and one started mid-blackout picks up the time remaining.
    /// </summary>
    public static class FleetTrackingUtility
    {
        public static bool IsTracked(HuntdownDef def)
        {
            return def != null && def.HasModExtension<ModExtension_FleetTracking>();
        }

        /// <summary>進行中且依賴追緝網路的追殺。Active huntdowns that rely on the tracking network.</summary>
        public static IEnumerable<HuntdownInstance> ActiveTracked()
        {
            foreach (HuntdownInstance instance in HuntdownUtility.AllActive)
            {
                if (IsTracked(instance.def)) yield return instance;
            }
        }

        public static bool AnyActive => ActiveTracked().Any();

        /// <summary>真空期剩餘 ticks；不在真空期為 0。Ticks left in the blackout; 0 outside one.</summary>
        public static int BlackoutTicksLeft
        {
            get
            {
                int left = 0;
                foreach (HuntdownInstance instance in ActiveTracked())
                {
                    left = Mathf.Max(left, instance.SuspendedTicksLeft);
                }
                return left;
            }
        }

        public static bool InBlackout => BlackoutTicksLeft > 0;

        /// <summary>
        /// 讓所有進行中的艦隊追殺暫停到同一個結束點：目前最晚的結束點（或現在）再加 ticks。回傳剩餘 ticks。
        /// Suspends every active fleet hunt until one shared end: the latest current end (or now) plus ticks. Returns the ticks left.
        /// </summary>
        public static int SuspendAll(int ticks)
        {
            List<HuntdownInstance> active = ActiveTracked().ToList();
            if (active.Count == 0) return 0;
            int now = Find.TickManager.TicksGame;
            int start = now;
            foreach (HuntdownInstance instance in active)
            {
                start = Mathf.Max(start, instance.suspendedUntilTick);
            }
            int end = start + Mathf.Max(0, ticks);
            foreach (HuntdownInstance instance in active)
            {
                // Suspend 以 max(now, 自己的結束點) 為起點疊加。Suspend stacks onto max(now, its own end).
                HuntdownUtility.Suspend(instance.def, end - Mathf.Max(now, instance.suspendedUntilTick));
            }
            return end - now;
        }

        /// <summary>立即結束所有艦隊追殺的暫停（除錯用）。Ends every fleet hunt's suspension at the next check (debug).</summary>
        public static void ResumeAllNow()
        {
            foreach (HuntdownInstance instance in ActiveTracked().ToList())
            {
                HuntdownUtility.ResumeNow(instance.def);
            }
        }

        /// <summary>
        /// 剛啟動的追殺承接其他艦隊追殺的剩餘真空期。在 HuntdownUtility.Start 之後呼叫。
        /// A just-started huntdown picks up the blackout the other fleet hunts are in. Call after HuntdownUtility.Start.
        /// </summary>
        public static void InheritBlackout(HuntdownDef def)
        {
            HuntdownInstance self = HuntdownUtility.GetInstance(def);
            if (self == null || !IsTracked(def)) return;
            int left = 0;
            foreach (HuntdownInstance instance in ActiveTracked())
            {
                if (instance != self) left = Mathf.Max(left, instance.SuspendedTicksLeft);
            }
            if (left > self.SuspendedTicksLeft)
            {
                HuntdownUtility.Suspend(def, left - self.SuspendedTicksLeft);
            }
        }
    }
}
